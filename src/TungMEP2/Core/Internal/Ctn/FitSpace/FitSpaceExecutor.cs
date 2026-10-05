using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceExecutor
{
	private const double MillimetresPerFoot = 304.8;

	private sealed class FitSpacePendingTransactionException : InvalidOperationException
	{
		internal FitSpacePendingTransactionException()
			: base("Failure processing của Revit đang Pending; FitSpace đã dừng và không chạy cụm kế tiếp.")
		{
		}
	}

	private sealed class FitSpaceFailurePreprocessor : IFailuresPreprocessor
	{
		public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
		{
			bool hasError = false;
			foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
			{
				if (failure.GetSeverity() == FailureSeverity.Warning)
				{
					failuresAccessor.DeleteWarning(failure);
				}
				else
				{
					hasError = true;
				}
			}
			return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
		}
	}

	private sealed class FitSpacePendingTransactionFinalizer : ITransactionFinalizer
	{
		private readonly TransactionGroup _group;
		private bool _armed;

		internal FitSpacePendingTransactionFinalizer(TransactionGroup group)
		{
			_group = group;
		}

		internal void ArmForPending()
		{
			_armed = true;
		}

		public void OnCommitted(Document document, string transactionName)
		{
			CloseGroupAfterPending();
		}

		public void OnRolledBack(Document document, string transactionName)
		{
			CloseGroupAfterPending();
		}

		private void CloseGroupAfterPending()
		{
			if (!_armed)
			{
				return;
			}
			try
			{
				if (_group.GetStatus() == TransactionStatus.Started)
				{
					_group.RollBack();
				}
			}
			finally
			{
				_group.Dispose();
			}
		}
	}

	internal static bool TryExecute(
		UIDocument uiDocument,
		FitSpacePlan plan,
		out FitSpaceExecutionReport report,
		out string error)
	{
		report = new FitSpaceExecutionReport(0, 0, 0, 0, Array.Empty<FitSpaceReportRow>(), Array.Empty<FitSpaceReportRow>());
		error = string.Empty;
		Document document = uiDocument?.Document;
		if (document == null || plan == null || plan.Entries.Count == 0)
		{
			error = "Không có cụm FitSpace để thực thi.";
			return false;
		}

		int adjusted = 0;
		int alreadyNormalized = 0;
		int skippedCount = 0;
		int rolledBackCount = 0;
		List<FitSpaceReportRow> skipped = new List<FitSpaceReportRow>();
		List<FitSpaceReportRow> rolledBack = new List<FitSpaceReportRow>();
		bool hasCommittedTransaction = false;

		TransactionGroup group = new TransactionGroup(document, "TungMEP2 - FitSpace");
		if (group.Start() != TransactionStatus.Started)
		{
			group.Dispose();
			error = "Không thể bắt đầu nhóm transaction FitSpace.";
			return false;
		}

		try
		{
			foreach (FitSpacePlanEntry entry in plan.Entries)
			{
				if (entry.Target.IsAlreadyNormalized)
				{
					alreadyNormalized++;
					continue;
				}

				Transaction transaction = new Transaction(document, $"FitSpace cluster {entry.Cluster.Key}");
				if (transaction.Start() != TransactionStatus.Started)
				{
					transaction.Dispose();
					skippedCount++;
					skipped.Add(new FitSpaceReportRow(entry.Cluster.Key, "Không thể bắt đầu transaction cho cụm."));
					continue;
				}
				FitSpacePendingTransactionFinalizer finalizer = new FitSpacePendingTransactionFinalizer(group);
				ConfigureFailureHandling(transaction, finalizer);

				try
				{
					if (!TryRevalidateBeforeDisconnect(document, entry, out string reason))
					{
						transaction.RollBack();
						transaction.Dispose();
						skippedCount++;
						skipped.Add(new FitSpaceReportRow(entry.Cluster.Key, reason));
						continue;
					}

					FitSpaceCluster executionCluster = entry.Cluster.WithExternalElementSnapshots(
						FitSpaceCollector.CaptureExternalNetworkForExecution(document, entry.Cluster));
					FitSpacePlanEntry executionEntry = new FitSpacePlanEntry(executionCluster, entry.Target);
					ExecuteCluster(document, executionEntry);
					if (!ValidatePostconditions(document, executionEntry, out reason))
					{
						throw new InvalidOperationException(reason);
					}

					TransactionStatus commitStatus = transaction.Commit();
					if (commitStatus == TransactionStatus.Pending)
					{
						finalizer.ArmForPending();
						throw new FitSpacePendingTransactionException();
					}
					if (commitStatus != TransactionStatus.Committed)
					{
						throw new InvalidOperationException("Revit không commit được cụm FitSpace.");
					}
					transaction.Dispose();
					hasCommittedTransaction = true;
					adjusted++;
				}
				catch (FitSpacePendingTransactionException)
				{
					throw;
				}
				catch (Exception exception)
				{
					if (transaction.GetStatus() == TransactionStatus.Started)
					{
						transaction.RollBack();
					}
					transaction.Dispose();
					rolledBackCount++;
					rolledBack.Add(new FitSpaceReportRow(entry.Cluster.Key, exception.Message));
				}
			}

			if (hasCommittedTransaction)
			{
				if (group.Assimilate() != TransactionStatus.Committed)
				{
					throw new InvalidOperationException("Không thể gộp lịch sử Undo FitSpace.");
				}
			}
			else
			{
				group.RollBack();
			}
			group.Dispose();
		}
		catch (FitSpacePendingTransactionException exception)
		{
			// The armed transaction finalizer owns group rollback/disposal after
			// asynchronous Revit failure handling completes. Do not touch the group here.
			error = exception.Message;
			report = new FitSpaceExecutionReport(0, alreadyNormalized, skippedCount, rolledBackCount, skipped, rolledBack);
			return false;
		}
		catch (Exception exception)
		{
			if (group.GetStatus() == TransactionStatus.Started)
			{
				group.RollBack();
			}
			group.Dispose();
			error = exception.Message;
			report = new FitSpaceExecutionReport(0, alreadyNormalized, skippedCount, rolledBackCount, skipped, rolledBack);
			return false;
		}

		report = new FitSpaceExecutionReport(adjusted, alreadyNormalized, skippedCount, rolledBackCount, skipped, rolledBack);
		if (!hasCommittedTransaction && alreadyNormalized == 0)
		{
			error = rolledBack.FirstOrDefault()?.Reason ?? skipped.FirstOrDefault()?.Reason ?? "Không có cụm nào được chỉnh.";
			return false;
		}
		return true;
	}

	private static bool TryRevalidateBeforeDisconnect(Document document, FitSpacePlanEntry entry, out string error)
	{
		FitSpaceCluster cluster = entry.Cluster;
		if (!ValidateElementIdentity(document, cluster, out error))
		{
			return false;
		}
		foreach (FitSpaceElementSnapshot snapshot in cluster.ElementSnapshots)
		{
			Element element = document.GetElement(snapshot.Id);
			if (element.Pinned || element.GroupId != ElementId.InvalidElementId || !element.IsModifiable)
			{
				error = "Phần tử đã bị Pin, đưa vào Group hoặc khóa sau bước phân tích.";
				return false;
			}
			if (document.IsWorkshared &&
				WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser)
			{
				error = "Phần tử vừa được người dùng khác sở hữu.";
				return false;
			}
			ElementId activeDesignOptionId = DesignOption.GetActiveDesignOptionId(document);
			ElementId designOptionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
			if (designOptionId != ElementId.InvalidElementId && designOptionId != activeDesignOptionId)
			{
				error = "Design option của cụm không còn hoạt động.";
				return false;
			}
		}

		foreach (FitSpaceConnectorBinding binding in cluster.LocalBindings)
		{
			Connector first = FitSpaceConnectorService.FindByKey(document, binding.First);
			Connector second = FitSpaceConnectorService.FindByKey(document, binding.Second);
			if (first == null || second == null ||
				first.Origin.DistanceTo(binding.First.OriginalOrigin) > 0.1 / MillimetresPerFoot ||
				second.Origin.DistanceTo(binding.Second.OriginalOrigin) > 0.1 / MillimetresPerFoot ||
				!FitSpaceConnectorService.IsConnected(first, second))
			{
				error = "ID, vị trí hoặc kết nối connector đã thay đổi sau bước phân tích.";
				return false;
			}
			if (!ValidateBindingGeometry(document, binding, out error))
			{
				return false;
			}
		}

		return ValidateFarEndpoints(document, cluster, 0.1 / MillimetresPerFoot, out error) &&
			ValidateExternalNetwork(document, cluster, 0.1 / MillimetresPerFoot, out error) &&
			ValidateConnectorState(document, cluster, out error);
	}

	private static void ExecuteCluster(Document document, FitSpacePlanEntry entry)
	{
		foreach (FitSpaceConnectorBinding binding in entry.Cluster.LocalBindings)
		{
			if (!FitSpaceConnectorService.TryDisconnect(document, binding, out string reason))
			{
				throw new InvalidOperationException(reason);
			}
		}

		ElementTransformUtils.MoveElement(document, entry.Cluster.Elbow1Id, entry.Target.Elbow1Translation);
		ElementTransformUtils.MoveElement(document, entry.Cluster.Elbow2Id, entry.Target.Elbow2Translation);
		document.Regenerate();

		SetPipeLine(document, entry.Cluster.OuterPipe1Id, entry.Target.OuterPipe1Line);
		SetPipeLine(document, entry.Cluster.MiddlePipeId, entry.Target.MiddlePipeLine);
		SetPipeLine(document, entry.Cluster.OuterPipe2Id, entry.Target.OuterPipe2Line);
		document.Regenerate();

		foreach (FitSpaceConnectorBinding binding in entry.Cluster.LocalBindings)
		{
			if (!FitSpaceConnectorService.TryReconnect(document, binding, out string reason))
			{
				throw new InvalidOperationException(reason);
			}
		}
		document.Regenerate();
	}

	private static void SetPipeLine(Document document, ElementId pipeId, Line line)
	{
		if (!(document.GetElement(pipeId)?.Location is LocationCurve locationCurve))
		{
			throw new InvalidOperationException("Không truy cập được LocationCurve của Pipe.");
		}
		locationCurve.Curve = line;
	}

	private static void ConfigureFailureHandling(
		Transaction transaction,
		FitSpacePendingTransactionFinalizer finalizer)
	{
		FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
		options.SetFailuresPreprocessor(new FitSpaceFailurePreprocessor());
		options.SetTransactionFinalizer(finalizer);
		options.SetClearAfterRollback(true);
		options.SetForcedModalHandling(true);
		transaction.SetFailureHandlingOptions(options);
	}

	private static bool ValidatePostconditions(Document document, FitSpacePlanEntry entry, out string error)
	{
		double farTolerance = 0.1 / MillimetresPerFoot;
		double apexTolerance = 0.5 / MillimetresPerFoot;
		return ValidateElementIdentity(document, entry.Cluster, out error) &&
			ValidateFarEndpoints(document, entry.Cluster, farTolerance, out error) &&
			ValidateExternalNetwork(document, entry.Cluster, farTolerance, out error) &&
			ValidateConnectorBindings(document, entry.Cluster, out error) &&
			ValidateConnectorState(document, entry.Cluster, out error) &&
			ValidateOrientation(document, entry.Cluster, out error) &&
			ValidateTargetApexes(document, entry, apexTolerance, out error) &&
			ValidateMinimumLength(document, entry.Cluster, out error) &&
			ValidateTargetPipeLines(document, entry, apexTolerance, out error);
	}

	private static bool ValidateElementIdentity(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		foreach (FitSpaceElementSnapshot snapshot in cluster.ElementSnapshots)
		{
			if (!ValidateOneElementIdentity(document, snapshot, out error))
			{
				return false;
			}
		}
		return true;
	}

	private static bool ValidateOneElementIdentity(
		Document document,
		FitSpaceElementSnapshot snapshot,
		out string error)
	{
		error = string.Empty;
		Element element = document.GetElement(snapshot.Id);
		if (element == null || element.UniqueId != snapshot.UniqueId ||
			element.GetTypeId() != snapshot.TypeId || element.Category?.Id != snapshot.CategoryId ||
			(element.DesignOption?.Id ?? ElementId.InvalidElementId) != snapshot.DesignOptionId)
		{
			error = "ElementId, UniqueId, type, category hoặc design option không còn nguyên vẹn.";
			return false;
		}

		double? diameter = ReadDouble(element, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		double? slope = ReadDouble(element, BuiltInParameter.RBS_PIPE_SLOPE);
		ElementId systemType = ReadElementId(element, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
		if (!EqualNullable(diameter, snapshot.Diameter, 1e-9) ||
			!EqualNullable(slope, snapshot.Slope, 1e-9) || systemType != snapshot.SystemTypeId)
		{
			error = "DN, slope hoặc system type của phần tử đã thay đổi.";
			return false;
		}
		return true;
	}

	private static bool ValidateFarEndpoints(Document document, FitSpaceCluster cluster, double tolerance, out string error)
	{
		error = string.Empty;
		foreach (FitSpaceFarEndSnapshot far in new[] { cluster.FarEnd1, cluster.FarEnd2 })
		{
			Connector connector = FitSpaceConnectorService.FindByKey(document, far.PipeConnector);
			if (connector == null || connector.Origin.DistanceTo(far.Point) > tolerance)
			{
				error = "Đầu xa Pipe ngoài đã bị dịch chuyển.";
				return false;
			}
			IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector);
			bool connectionStatePreserved = far.WasConnected
				? far.ExternalBinding != null && references.Count == 1 &&
					FitSpaceConnectorService.IsConnected(document, far.ExternalBinding)
				: references.Count == 0;
			if (!connectionStatePreserved)
			{
				error = "Trạng thái kết nối ở đầu xa Pipe ngoài đã thay đổi.";
				return false;
			}
		}
		return true;
	}

	private static bool ValidateConnectorBindings(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		if (cluster.LocalBindings.All(binding => FitSpaceConnectorService.IsConnected(document, binding)))
		{
			return true;
		}
		error = "Không khôi phục đủ bốn kết nối cục bộ.";
		return false;
	}

	private static bool ValidateBindingGeometry(Document document, FitSpaceConnectorBinding binding, out string error)
	{
		return FitSpaceConnectorService.ValidateBindingGeometry(
			document,
			binding,
			0.1 / MillimetresPerFoot,
			out error);
	}

	private static bool ValidateConnectorState(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		IEnumerable<FitSpaceConnectorKey> keys = cluster.LocalBindings
			.SelectMany(binding => new[] { binding.First, binding.Second })
			.Concat(new[] { cluster.FarEnd1.PipeConnector, cluster.FarEnd2.PipeConnector })
			.Concat(cluster.FarEnd1.ExternalBinding == null
				? Array.Empty<FitSpaceConnectorKey>()
				: new[] { cluster.FarEnd1.ExternalBinding.First, cluster.FarEnd1.ExternalBinding.Second })
			.Concat(cluster.FarEnd2.ExternalBinding == null
				? Array.Empty<FitSpaceConnectorKey>()
				: new[] { cluster.FarEnd2.ExternalBinding.First, cluster.FarEnd2.ExternalBinding.Second })
			.Concat(cluster.ExternalElementSnapshots.SelectMany(snapshot => snapshot.Connectors));
		foreach (FitSpaceConnectorKey key in keys
			.GroupBy(item => $"{item.OwnerUniqueId}:{item.ConnectorId}")
			.Select(group => group.First()))
		{
			Connector connector = FitSpaceConnectorService.FindByKey(document, key);
			ElementId systemId = connector?.MEPSystem?.Id ?? ElementId.InvalidElementId;
			if (connector == null || systemId != key.OriginalSystemId || connector.Shape != key.OriginalShape ||
				Math.Abs((connector.Shape == ConnectorProfileType.Round ? connector.Radius : 0.0) - key.OriginalRadius) > 1e-9 ||
				Math.Abs((connector.Shape == ConnectorProfileType.Rectangular || connector.Shape == ConnectorProfileType.Oval ? connector.Width : 0.0) - key.OriginalWidth) > 1e-9 ||
				Math.Abs((connector.Shape == ConnectorProfileType.Rectangular || connector.Shape == ConnectorProfileType.Oval ? connector.Height : 0.0) - key.OriginalHeight) > 1e-9)
			{
				error = "MEPSystem, shape hoặc DN/kích thước connector đã thay đổi.";
				return false;
			}
		}
		return true;
	}

	private static bool ValidateExternalNetwork(
		Document document,
		FitSpaceCluster cluster,
		double tolerance,
		out string error)
	{
		error = string.Empty;
		foreach (FitSpaceExternalElementSnapshot snapshot in cluster.ExternalElementSnapshots)
		{
			if (!ValidateOneElementIdentity(document, snapshot.Element, out error))
			{
				error = "Mạng ngoài thay đổi identity/parameter: " + error;
				return false;
			}

			Element element = document.GetElement(snapshot.Element.Id);
			if (!ValidateExternalLocation(element, snapshot, tolerance) ||
				!ValidateExternalTransform(element, snapshot, tolerance))
			{
				error = "Revit đã dịch chuyển hoặc xoay phần tử ngoài cụm.";
				return false;
			}

			IReadOnlyList<Connector> currentConnectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(element);
			if (currentConnectors.Count != snapshot.Connectors.Count)
			{
				error = "Số connector của phần tử ngoài cụm đã thay đổi.";
				return false;
			}
			foreach (FitSpaceConnectorKey key in snapshot.Connectors)
			{
				Connector connector = FitSpaceConnectorService.FindByKey(document, key);
				if (connector == null || connector.Origin.DistanceTo(key.OriginalOrigin) > tolerance ||
					!SameDirection(connector.CoordinateSystem?.BasisX, key.OriginalBasisX, 1e-8) ||
					!SameDirection(connector.CoordinateSystem?.BasisY, key.OriginalBasisY, 1e-8) ||
					!SameDirection(connector.CoordinateSystem?.BasisZ, key.OriginalBasisZ, 1e-8))
				{
					error = "Connector của mạng ngoài đã dịch chuyển hoặc đổi orientation.";
					return false;
				}
			}
		}
		return true;
	}

	private static bool ValidateExternalLocation(
		Element element,
		FitSpaceExternalElementSnapshot snapshot,
		double tolerance)
	{
		if (snapshot.LocationKind.Length == 0)
		{
			return true;
		}
		if (element.Location is LocationCurve locationCurve && locationCurve.Curve?.IsBound == true)
		{
			Curve curve = locationCurve.Curve;
			return curve.GetType().FullName == snapshot.LocationKind &&
				SamePoint(curve.GetEndPoint(0), snapshot.LocationStart, tolerance) &&
				SamePoint(curve.Evaluate(0.5, true), snapshot.LocationMiddle, tolerance) &&
				SamePoint(curve.GetEndPoint(1), snapshot.LocationEnd, tolerance);
		}
		if (element.Location is LocationPoint locationPoint && snapshot.LocationKind == nameof(LocationPoint))
		{
			return SamePoint(locationPoint.Point, snapshot.LocationStart, tolerance) &&
				EqualNullable(locationPoint.Rotation, snapshot.LocationRotation, 1e-9);
		}
		return false;
	}

	private static bool ValidateExternalTransform(
		Element element,
		FitSpaceExternalElementSnapshot snapshot,
		double tolerance)
	{
		if (snapshot.TransformOrigin == null)
		{
			return true;
		}
		Transform transform = (element as FamilyInstance)?.GetTransform();
		return transform != null && SamePoint(transform.Origin, snapshot.TransformOrigin, tolerance) &&
			SameDirection(transform.BasisX, snapshot.TransformBasisX, 1e-8) &&
			SameDirection(transform.BasisY, snapshot.TransformBasisY, 1e-8) &&
			SameDirection(transform.BasisZ, snapshot.TransformBasisZ, 1e-8);
	}

	private static bool ValidateOrientation(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		foreach (FitSpaceConnectorKey key in cluster.LocalBindings
			.SelectMany(binding => new[] { binding.First, binding.Second })
			.Where(key => key.OwnerId == cluster.Elbow1Id || key.OwnerId == cluster.Elbow2Id)
			.GroupBy(key => $"{key.OwnerUniqueId}:{key.ConnectorId}")
			.Select(group => group.First()))
		{
			Connector connector = FitSpaceConnectorService.FindByKey(document, key);
			XYZ basis = connector?.CoordinateSystem?.BasisZ;
			if (basis == null || key.OriginalBasisZ == null || basis.Normalize().DotProduct(key.OriginalBasisZ.Normalize()) < 1.0 - 1e-8)
			{
				error = "Hướng connector elbow đã thay đổi; fitting không còn chỉ được tịnh tiến.";
				return false;
			}
		}
		return true;
	}

	private static bool ValidateTargetApexes(Document document, FitSpacePlanEntry entry, double tolerance, out string error)
	{
		error = string.Empty;
		Connector elbow1Outer = ConnectorOnOwner(document, entry.Cluster.LocalBindings[0], entry.Cluster.Elbow1Id);
		Connector elbow1Inner = ConnectorOnOwner(document, entry.Cluster.LocalBindings[1], entry.Cluster.Elbow1Id);
		Connector elbow2Inner = ConnectorOnOwner(document, entry.Cluster.LocalBindings[2], entry.Cluster.Elbow2Id);
		Connector elbow2Outer = ConnectorOnOwner(document, entry.Cluster.LocalBindings[3], entry.Cluster.Elbow2Id);
		if (!FitSpaceGeometry.TryFindElbowApex(elbow1Outer, elbow1Inner, tolerance, out XYZ apex1) ||
			!FitSpaceGeometry.TryFindElbowApex(elbow2Inner, elbow2Outer, tolerance, out XYZ apex2) ||
			apex1.DistanceTo(entry.Target.TargetApex1) > tolerance || apex2.DistanceTo(entry.Target.TargetApex2) > tolerance)
		{
			error = "Đỉnh elbow sau chỉnh không đạt vị trí mục tiêu.";
			return false;
		}
		return true;
	}

	private static bool ValidateMinimumLength(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		double minimum = Math.Max(document.Application.ShortCurveTolerance * 2.0, 5.0 / MillimetresPerFoot);
		foreach (ElementId id in new[] { cluster.OuterPipe1Id, cluster.MiddlePipeId, cluster.OuterPipe2Id })
		{
			Line line = (document.GetElement(id)?.Location as LocationCurve)?.Curve as Line;
			if (line == null || line.Length < minimum)
			{
				error = "Một đoạn Pipe sau chỉnh ngắn hơn giới hạn an toàn.";
				return false;
			}
		}
		return true;
	}

	private static bool ValidateTargetPipeLines(Document document, FitSpacePlanEntry entry, double tolerance, out string error)
	{
		error = string.Empty;
		ElementId[] ids = { entry.Cluster.OuterPipe1Id, entry.Cluster.MiddlePipeId, entry.Cluster.OuterPipe2Id };
		Line[] targets = { entry.Target.OuterPipe1Line, entry.Target.MiddlePipeLine, entry.Target.OuterPipe2Line };
		for (int index = 0; index < ids.Length; index++)
		{
			Line actual = (document.GetElement(ids[index])?.Location as LocationCurve)?.Curve as Line;
			if (actual == null || !SameUnorderedEndpoints(actual, targets[index], tolerance))
			{
				error = "LocationCurve của Pipe không khớp hình học mục tiêu.";
				return false;
			}
		}
		return true;
	}

	private static Connector ConnectorOnOwner(Document document, FitSpaceConnectorBinding binding, ElementId ownerId)
	{
		FitSpaceConnectorKey key = binding.First.OwnerId == ownerId ? binding.First : binding.Second;
		return key.OwnerId == ownerId ? FitSpaceConnectorService.FindByKey(document, key) : null;
	}

	private static bool SameUnorderedEndpoints(Line actual, Line target, double tolerance)
	{
		XYZ a0 = actual.GetEndPoint(0);
		XYZ a1 = actual.GetEndPoint(1);
		XYZ t0 = target.GetEndPoint(0);
		XYZ t1 = target.GetEndPoint(1);
		return (a0.DistanceTo(t0) <= tolerance && a1.DistanceTo(t1) <= tolerance) ||
			(a0.DistanceTo(t1) <= tolerance && a1.DistanceTo(t0) <= tolerance);
	}

	private static double? ReadDouble(Element element, BuiltInParameter parameter)
	{
		Parameter value = element.get_Parameter(parameter);
		return value != null && value.StorageType == StorageType.Double ? value.AsDouble() : null;
	}

	private static ElementId ReadElementId(Element element, BuiltInParameter parameter)
	{
		Parameter value = element.get_Parameter(parameter);
		return value != null && value.StorageType == StorageType.ElementId ? value.AsElementId() : ElementId.InvalidElementId;
	}

	private static bool EqualNullable(double? first, double? second, double tolerance)
	{
		return first.HasValue == second.HasValue && (!first.HasValue || Math.Abs(first.Value - second.Value) <= tolerance);
	}

	private static bool SamePoint(XYZ first, XYZ second, double tolerance)
	{
		return first == null ? second == null : second != null && first.DistanceTo(second) <= tolerance;
	}

	private static bool SameDirection(XYZ first, XYZ second, double tolerance)
	{
		return first == null ? second == null : second != null &&
			first.Normalize().DotProduct(second.Normalize()) >= 1.0 - tolerance;
	}
}
