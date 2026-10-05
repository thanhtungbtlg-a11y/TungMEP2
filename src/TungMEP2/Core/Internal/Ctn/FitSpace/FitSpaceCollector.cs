using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceCollector
{
	internal static bool TryCollectMany(
		Document document,
		IReadOnlyCollection<ElementId> selectedIds,
		out IReadOnlyList<FitSpaceCluster> clusters,
		out FitSpaceCollectionReport report,
		out string error)
	{
		List<FitSpaceCluster> candidates = new List<FitSpaceCluster>();
		List<FitSpaceReportRow> skipped = new List<FitSpaceReportRow>();
		Dictionary<long, ElementId> seedIds = new Dictionary<long, ElementId>();
		error = string.Empty;
		clusters = Array.Empty<FitSpaceCluster>();

		if (document == null || selectedIds == null || selectedIds.Count == 0)
		{
			report = new FitSpaceCollectionReport(skipped);
			error = "Chưa chọn Pipe giữa hoặc elbow của cụm cần chỉnh.";
			return false;
		}

		long seedKey = 0;
		foreach (ElementId selectedId in selectedIds)
		{
			seedKey++;
			seedIds[seedKey] = selectedId;
			Element selected = document.GetElement(selectedId);
			if (!TryResolveMiddlePipe(selected, out Pipe middlePipe, out string reason) ||
				!TryCollectCluster(document, seedKey, middlePipe, out FitSpaceCluster cluster, out reason))
			{
				skipped.Add(new FitSpaceReportRow(seedKey, reason));
				continue;
			}
			candidates.Add(cluster);
		}

		List<FitSpaceOwnershipSet> ownershipSets = candidates
			.Select(candidate => new FitSpaceOwnershipSet(
				candidate.Key,
				candidate.OwnedElementIds.Select(IdValue).ToArray()))
			.ToList();
		FitSpaceOwnershipPolicy.Classify(ownershipSets, out IReadOnlyList<long> duplicateKeys, out IReadOnlyList<FitSpaceOwnershipOverlap> overlaps);
		if (overlaps.Count > 0)
		{
			report = new FitSpaceCollectionReport(skipped);
			error = "Các cụm FitSpace đang dùng chung một phần tử. Hãy chọn từng cụm độc lập.";
			return false;
		}

		HashSet<long> duplicates = new HashSet<long>(duplicateKeys);
		FitSpaceCluster[] uniqueClusters = candidates.Where(candidate => !duplicates.Contains(candidate.Key)).ToArray();
		HashSet<long> allOwnedIds = new HashSet<long>(uniqueClusters
			.SelectMany(candidate => candidate.OwnedElementIds)
			.Select(IdValue));
		clusters = uniqueClusters;
		skipped = skipped.Where(row =>
			!seedIds.TryGetValue(row.Key, out ElementId selectedId) || !allOwnedIds.Contains(IdValue(selectedId)))
			.ToList();
		report = new FitSpaceCollectionReport(skipped);
		if (clusters.Count == 0)
		{
			error = skipped.FirstOrDefault()?.Reason ?? "Không tìm thấy cụm Pipe–Elbow hợp lệ.";
			return false;
		}

		return true;
	}

	private static bool TryResolveMiddlePipe(Element selected, out Pipe middlePipe, out string error)
	{
		middlePipe = null;
		error = string.Empty;
		if (selected is Pipe pipe)
		{
			if (!HasElbowAtBothEnds(pipe))
			{
				error = "Pipe đã chọn không phải Pipe giữa hai elbow.";
				return false;
			}
			middlePipe = pipe;
			return true;
		}

		if (!IsTwoPortElbow(selected, out FamilyInstance selectedElbow, out error))
		{
			return false;
		}

		List<Pipe> candidates = FitSpaceConnectorService.GetPhysicalPipingConnectors(selectedElbow)
			.SelectMany(FitSpaceConnectorService.GetConnectedPhysicalReferences)
			.Select(reference => reference.Owner)
			.OfType<Pipe>()
			.Where(HasElbowAtBothEnds)
			.GroupBy(candidate => IdValue(candidate.Id))
			.Select(group => group.First())
			.ToList();
		if (candidates.Count != 1)
		{
			error = candidates.Count == 0
				? "Elbow đã chọn không thuộc cụm có Pipe giữa hai elbow."
				: "Elbow đã chọn cho ra nhiều Pipe giữa; không thể xác định cụm duy nhất.";
			return false;
		}

		middlePipe = candidates[0];
		return true;
	}

	private static bool TryCollectCluster(
		Document document,
		long key,
		Pipe middlePipe,
		out FitSpaceCluster cluster,
		out string error)
	{
		cluster = null;
		error = string.Empty;
		if (!TryGetStraightLine(middlePipe, out _))
		{
			error = "Pipe giữa phải là đoạn Pipe thẳng.";
			return false;
		}

		IReadOnlyList<Connector> middleConnectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(middlePipe);
		if (middleConnectors.Count != 2)
		{
			error = "Pipe giữa phải có đúng hai connector đầu mút.";
			return false;
		}

		if (!TryGetSingleElbowReference(middleConnectors[0], out FamilyInstance elbow1, out Connector elbow1Inner, out error) ||
			!TryGetSingleElbowReference(middleConnectors[1], out FamilyInstance elbow2, out Connector elbow2Inner, out error))
		{
			return false;
		}
		if (elbow1.Id == elbow2.Id)
		{
			error = "Hai đầu Pipe giữa đang nối cùng một elbow.";
			return false;
		}

		if (!TryGetOuterPipe(elbow1, elbow1Inner, out Pipe outer1, out Connector elbow1Outer, out Connector outer1Local, out error) ||
			!TryGetOuterPipe(elbow2, elbow2Inner, out Pipe outer2, out Connector elbow2Outer, out Connector outer2Local, out error))
		{
			return false;
		}
		if (outer1.Id == outer2.Id || outer1.Id == middlePipe.Id || outer2.Id == middlePipe.Id)
		{
			error = "Ba đoạn Pipe trong cụm phải là ba phần tử khác nhau.";
			return false;
		}

		if (!TryCaptureFarEnd(outer1, outer1Local, out FitSpaceFarEndSnapshot far1, out error) ||
			!TryCaptureFarEnd(outer2, outer2Local, out FitSpaceFarEndSnapshot far2, out error))
		{
			return false;
		}

		FitSpaceConnectorBinding[] localBindings =
		{
			Bind(outer1Local, elbow1Outer),
			Bind(elbow1Inner, middleConnectors[0]),
			Bind(middleConnectors[1], elbow2Inner),
			Bind(elbow2Outer, outer2Local)
		};
		double connectorTolerance = 0.1 / 304.8;
		foreach (FitSpaceConnectorBinding binding in localBindings)
		{
			if (!FitSpaceConnectorService.ValidateBindingGeometry(
				document,
				binding,
				connectorTolerance,
				out error))
			{
				return false;
			}
		}
		Element[] elements = { middlePipe, elbow1, elbow2, outer1, outer2 };
		cluster = new FitSpaceCluster(
			key,
			middlePipe.Id,
			elbow1.Id,
			elbow2.Id,
			outer1.Id,
			outer2.Id,
			localBindings,
			far1,
			far2,
			elements.Select(element => new FitSpaceElementSnapshot(element)).ToArray());
		return true;
	}

	private static bool TryGetSingleElbowReference(
		Connector pipeConnector,
		out FamilyInstance elbow,
		out Connector elbowConnector,
		out string error)
	{
		elbow = null;
		elbowConnector = null;
		error = string.Empty;
		IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(pipeConnector);
		if (references.Count != 1 || !IsTwoPortElbow(references[0].Owner, out elbow, out error))
		{
			error = "Mỗi đầu Pipe giữa phải nối trực tiếp đúng một elbow hai cổng.";
			return false;
		}
		elbowConnector = references[0];
		return true;
	}

	private static bool TryGetOuterPipe(
		FamilyInstance elbow,
		Connector innerConnector,
		out Pipe outerPipe,
		out Connector elbowOuter,
		out Connector outerLocal,
		out string error)
	{
		outerPipe = null;
		elbowOuter = null;
		outerLocal = null;
		error = string.Empty;
		elbowOuter = FitSpaceConnectorService.GetPhysicalPipingConnectors(elbow)
			.SingleOrDefault(connector => connector.Id != innerConnector.Id);
		if (elbowOuter == null)
		{
			error = "Không xác định được cổng ngoài của elbow.";
			return false;
		}

		IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(elbowOuter);
		if (references.Count != 1 || !(references[0].Owner is Pipe pipe) || !TryGetStraightLine(pipe, out _))
		{
			error = "Cổng ngoài elbow phải nối đúng một đoạn Pipe thẳng.";
			return false;
		}

		outerPipe = pipe;
		outerLocal = references[0];
		return true;
	}

	private static bool TryCaptureFarEnd(
		Pipe pipe,
		Connector localConnector,
		out FitSpaceFarEndSnapshot snapshot,
		out string error)
	{
		snapshot = null;
		error = string.Empty;
		Connector farConnector = FitSpaceConnectorService.GetPhysicalPipingConnectors(pipe)
			.SingleOrDefault(connector => connector.Id != localConnector.Id);
		if (farConnector == null)
		{
			error = "Không xác định được đầu xa của Pipe ngoài.";
			return false;
		}

		IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(farConnector);
		if (references.Count > 1)
		{
			error = "Đầu xa Pipe ngoài có nhiều hơn một kết nối vật lý.";
			return false;
		}

		FitSpaceConnectorBinding external = references.Count == 1 ? Bind(farConnector, references[0]) : null;
		snapshot = new FitSpaceFarEndSnapshot(pipe.Id, FitSpaceConnectorService.CreateKey(farConnector), farConnector.Origin, external);
		return true;
	}

	private static bool IsTwoPortElbow(Element element, out FamilyInstance elbow, out string error)
	{
		elbow = element as FamilyInstance;
		error = string.Empty;
		IReadOnlyList<Connector> physicalConnectors = elbow == null
			? Array.Empty<Connector>()
			: FitSpaceConnectorService.GetPhysicalPipingConnectors(elbow);
		bool valid = elbow != null &&
			elbow.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting &&
			elbow.MEPModel is MechanicalFitting mechanicalFitting &&
			mechanicalFitting.PartType == PartType.Elbow &&
			physicalConnectors.Count == 2 &&
			physicalConnectors.All(connector => connector.Domain == Domain.DomainPiping &&
				connector.ConnectorType == ConnectorType.End);
		if (!valid)
		{
			error = "Đối tượng phải là elbow ống có đúng hai connector piping vật lý.";
		}
		return valid;
	}

	private static bool HasElbowAtBothEnds(Pipe pipe)
	{
		IReadOnlyList<Connector> connectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(pipe);
		return connectors.Count == 2 && connectors.All(connector =>
		{
			IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector);
			return references.Count == 1 && IsTwoPortElbow(references[0].Owner, out _, out _);
		});
	}

	private static bool TryGetStraightLine(Pipe pipe, out Line line)
	{
		line = (pipe.Location as LocationCurve)?.Curve as Line;
		return line != null && line.IsBound;
	}

	private static FitSpaceConnectorBinding Bind(Connector first, Connector second)
	{
		return new FitSpaceConnectorBinding(
			FitSpaceConnectorService.CreateKey(first),
			FitSpaceConnectorService.CreateKey(second));
	}

	internal static IReadOnlyList<FitSpaceExternalElementSnapshot> CaptureExternalNetworkForExecution(
		Document document,
		FitSpaceCluster cluster)
	{
		return CaptureExternalNetwork(
			document,
			cluster,
			new HashSet<long>(cluster.OwnedElementIds.Select(IdValue)));
	}

	private static IReadOnlyList<FitSpaceExternalElementSnapshot> CaptureExternalNetwork(
		Document document,
		FitSpaceCluster cluster,
		IReadOnlyCollection<long> excludedElementIds)
	{
		HashSet<long> excluded = new HashSet<long>(excludedElementIds ?? Array.Empty<long>());
		HashSet<long> visited = new HashSet<long>();
		Queue<Element> pending = new Queue<Element>();
		foreach (FitSpaceFarEndSnapshot far in new[] { cluster.FarEnd1, cluster.FarEnd2 })
		{
			Connector farConnector = FitSpaceConnectorService.FindByKey(document, far.PipeConnector);
			foreach (Connector reference in FitSpaceConnectorService.GetConnectedPhysicalReferences(farConnector))
			{
				pending.Enqueue(reference.Owner);
			}
		}

		List<FitSpaceExternalElementSnapshot> snapshots = new List<FitSpaceExternalElementSnapshot>();
		while (pending.Count > 0)
		{
			Element element = pending.Dequeue();
			long id = IdValue(element.Id);
			if (!visited.Add(id))
			{
				continue;
			}

			IReadOnlyList<Connector> connectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(element);
			if (!excluded.Contains(id))
			{
				snapshots.Add(CaptureExternalElement(element, connectors));
			}
			foreach (Connector connector in connectors)
			{
				foreach (Connector reference in FitSpaceConnectorService.GetConnectedPhysicalReferences(connector))
				{
					if (!visited.Contains(IdValue(reference.Owner.Id)))
					{
						pending.Enqueue(reference.Owner);
					}
				}
			}
		}
		return snapshots;
	}

	private static FitSpaceExternalElementSnapshot CaptureExternalElement(
		Element element,
		IReadOnlyList<Connector> connectors)
	{
		string locationKind = string.Empty;
		XYZ locationStart = null;
		XYZ locationMiddle = null;
		XYZ locationEnd = null;
		double? rotation = null;
		if (element.Location is LocationCurve locationCurve && locationCurve.Curve?.IsBound == true)
		{
			Curve curve = locationCurve.Curve;
			locationKind = curve.GetType().FullName;
			locationStart = curve.GetEndPoint(0);
			locationMiddle = curve.Evaluate(0.5, true);
			locationEnd = curve.GetEndPoint(1);
		}
		else if (element.Location is LocationPoint locationPoint)
		{
			locationKind = nameof(LocationPoint);
			locationStart = locationPoint.Point;
			rotation = locationPoint.Rotation;
		}

		Transform transform = (element as FamilyInstance)?.GetTransform();
		return new FitSpaceExternalElementSnapshot(
			new FitSpaceElementSnapshot(element),
			connectors.Select(FitSpaceConnectorService.CreateKey).ToArray(),
			locationKind,
			locationStart,
			locationMiddle,
			locationEnd,
			rotation,
			transform?.Origin,
			transform?.BasisX,
			transform?.BasisY,
			transform?.BasisZ);
	}

	private static long IdValue(ElementId id) => id.IntegerValue;
}
