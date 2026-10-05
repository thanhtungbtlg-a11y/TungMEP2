using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal sealed class PipeSpacingApplyReport
{
	internal int MovedClusterCount { get; set; }
	internal int StretchedBoundaryCount { get; set; }
	internal bool Cancelled { get; set; }
	internal double MinimumClearGap { get; set; }
	internal List<string> Warnings { get; } = new List<string>();
}

internal static class PipeSpacingExecutor
{
	private const double PointTolerance = 1E-05;

	internal static bool TryApply(
		UIDocument uiDocument,
		PipeSpacingPlan plan,
		bool previewBeforeApply,
		out PipeSpacingApplyReport report,
		out string error)
	{
		report = new PipeSpacingApplyReport();
		error = string.Empty;
		Document document = uiDocument?.Document;
		if (document == null || plan == null || document.IsReadOnly)
		{
			error = "Không có document hoặc phương án PipeSpacing có thể áp dụng.";
			return false;
		}
		if (uiDocument.ActiveView?.Id != plan.ActiveViewId)
		{
			error = "Active view đã thay đổi sau lúc phân tích. Hãy chạy lại PipeSpacing.";
			return false;
		}
		if (!TryRevalidate(document, plan, out error))
		{
			return false;
		}
		if (!PipeSpacingBoundaryService.TryCapture(document, plan, out IReadOnlyList<PipeSpacingBoundaryState> boundaryStates, out error))
		{
			return false;
		}

		Dictionary<long, OverrideGraphicSettings> originalOverrides = new Dictionary<long, OverrideGraphicSettings>();
		using Transaction transaction = new Transaction(document, "TungMEP2 - PipeSpacing");
		try
		{
			transaction.Start();
			foreach (PipeSpacingPlanEntry entry in plan.Entries.Where(item => !item.IsAnchor))
			{
				using SubTransaction subTransaction = new SubTransaction(document);
				subTransaction.Start();
				try
				{
					if (entry.Cluster.RigidElementIds.Count == 0)
					{
						throw new InvalidOperationException($"Cụm Pipe {entry.Cluster.SeedPipeId.Value} không có lõi dịch cứng.");
					}
					foreach (ElementId rigidId in entry.Cluster.RigidElementIds)
					{
						Element rigidElement = document.GetElement(rigidId);
						if (rigidElement == null || !rigidElement.IsModifiable)
						{
							throw new InvalidOperationException($"Phần tử {rigidId.Value} không thể chỉnh sửa trong transaction hiện tại.");
						}
					}
					ElementTransformUtils.MoveElements(
						document,
						entry.Cluster.RigidElementIds.ToList(),
						entry.MoveVector);

					foreach (PipeSpacingBoundaryState state in boundaryStates.Where(item =>
					         item.SeedPipeId.Value == entry.Cluster.SeedPipeId.Value))
					{
						if (!PipeSpacingBoundaryService.TryStretchAndVerify(
								document,
								state,
								entry.MoveVector,
								out string boundaryError))
						{
							throw new InvalidOperationException(boundaryError);
						}
						report.StretchedBoundaryCount++;
					}

					document.Regenerate();
					if (!ValidateMovedEntry(document, entry, out string validationError))
					{
						throw new InvalidOperationException(validationError);
					}
					if (subTransaction.Commit() != TransactionStatus.Committed)
					{
						throw new InvalidOperationException($"Revit không commit được cụm Pipe {entry.Cluster.SeedPipeId.Value}.");
					}
					report.MovedClusterCount++;
				}
				catch
				{
					if (subTransaction.GetStatus() == TransactionStatus.Started)
					{
						subTransaction.RollBack();
					}
					throw;
				}
			}

			document.Regenerate();
			if (!ValidateFinalClearances(document, plan, out string clearanceError))
			{
				throw new InvalidOperationException(clearanceError);
			}
			report.MinimumClearGap = CalculateMinimumClearGap(document, plan);

			if (previewBeforeApply)
			{
				if (!ApplyPreviewOverrides(uiDocument.ActiveView, plan, originalOverrides, out string previewError))
				{
					throw new InvalidOperationException(previewError);
				}
				document.Regenerate();
				uiDocument.RefreshActiveView();
				TaskDialog previewDialog = new TaskDialog("TungMEP2 - PipeSpacing Preview")
				{
					MainInstruction = "Kiểm tra vị trí dãy ống trước khi áp dụng",
					MainContent = "Màu xanh: cụm sẽ dịch chuyển. Màu xám: cụm neo được giữ cố định.",
					CommonButtons = TaskDialogCommonButtons.Cancel,
					DefaultButton = TaskDialogResult.Cancel
				};
				previewDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Áp dụng PipeSpacing");
				TaskDialogResult decision = previewDialog.Show();
				RestorePreviewOverrides(uiDocument.ActiveView, originalOverrides);
				document.Regenerate();
				uiDocument.RefreshActiveView();
				if (decision != TaskDialogResult.CommandLink1)
				{
					transaction.RollBack();
					report.Cancelled = true;
					report.MovedClusterCount = 0;
					report.StretchedBoundaryCount = 0;
					return true;
				}
			}

			// RestorePreviewOverrides must execute before the single outer Commit.
			if (transaction.Commit() != TransactionStatus.Committed)
			{
				throw new InvalidOperationException("Revit không commit được transaction PipeSpacing.");
			}
			return true;
		}
		catch (Exception exception)
		{
			if (transaction.GetStatus() == TransactionStatus.Started)
			{
				transaction.RollBack();
			}
			report.MovedClusterCount = 0;
			report.StretchedBoundaryCount = 0;
			error = "PipeSpacing đã rollback toàn bộ: " + exception.Message;
			return false;
		}
	}

	private static bool TryRevalidate(Document document, PipeSpacingPlan plan, out string error)
	{
		error = string.Empty;
		foreach (PipeSpacingPlanEntry entry in plan.Entries)
		{
			foreach (PipeSpacingElementSnapshot snapshot in entry.Snapshots)
			{
				Element element = document.GetElement(snapshot.ElementId);
				if (element == null || element.UniqueId != snapshot.UniqueId)
				{
					error = $"Phần tử {snapshot.ElementId.Value} đã bị xóa hoặc thay thế sau lúc xem trước.";
					return false;
				}
				if (element.GroupId != ElementId.InvalidElementId)
				{
					error = $"Phần tử {element.Id.Value} đã nằm trong Group; PipeSpacing không sửa cụm này.";
					return false;
				}
				if (!entry.IsAnchor && element.Pinned)
				{
					error = $"Phần tử {element.Id.Value} đã bị Pin; hãy bỏ Pin rồi chạy lại.";
					return false;
				}
				if (!entry.IsAnchor && document.IsWorkshared &&
				    WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser)
				{
					error = $"Phần tử {element.Id.Value} đang do người dùng khác sở hữu.";
					return false;
				}
			}
		}
		return true;
	}

	private static bool ValidateMovedEntry(
		Document document,
		PipeSpacingPlanEntry entry,
		out string error)
	{
		error = string.Empty;
		if (!(document.GetElement(entry.Cluster.SeedPipeId) is Pipe pipe) ||
		    pipe.UniqueId != entry.SeedPipeUniqueId ||
		    !(pipe.Location is LocationCurve { Curve: Line line }))
		{
			error = $"Không đọc lại được Pipe chính {entry.Cluster.SeedPipeId.Value} sau khi dịch.";
			return false;
		}

		XYZ expected0 = entry.OriginalStart.Add(entry.MoveVector);
		XYZ expected1 = entry.OriginalEnd.Add(entry.MoveVector);
		XYZ actual0 = line.GetEndPoint(0);
		XYZ actual1 = line.GetEndPoint(1);
		bool direct = actual0.DistanceTo(expected0) <= PointTolerance && actual1.DistanceTo(expected1) <= PointTolerance;
		bool reversed = actual0.DistanceTo(expected1) <= PointTolerance && actual1.DistanceTo(expected0) <= PointTolerance;
		if (!direct && !reversed)
		{
			error = $"Pipe chính {pipe.Id.Value} không giữ nguyên hình học/Z/slope khi dịch.";
			return false;
		}
		return true;
	}

	private static bool ValidateFinalClearances(
		Document document,
		PipeSpacingPlan plan,
		out string error)
	{
		error = string.Empty;
		double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
		double previousCoordinate = double.NegativeInfinity;
		foreach (PipeSpacingPlanEntry entry in plan.Entries)
		{
			if (!(document.GetElement(entry.Cluster.SeedPipeId) is Pipe pipe) ||
			    !(pipe.Location is LocationCurve { Curve: Line line }))
			{
				error = $"Không đọc được Pipe {entry.Cluster.SeedPipeId.Value} để kiểm tra khoảng cách cuối.";
				return false;
			}
			double coordinate = PipeSpacingGeometry.Midpoint(line).DotProduct(plan.SpacingAxis);
			if (Math.Abs(coordinate - entry.TargetCoordinate) > tolerance)
			{
				error = $"Pipe {pipe.Id.Value} lệch mục tiêu hơn 1 mm sau khi dàn khoảng cách.";
				return false;
			}
			if (coordinate <= previousCoordinate + 1E-09)
			{
				error = "Thứ tự dãy Pipe bị đảo hoặc chồng nhau sau khi áp dụng.";
				return false;
			}
			previousCoordinate = coordinate;
		}
		return true;
	}

	private static double CalculateMinimumClearGap(Document document, PipeSpacingPlan plan)
	{
		double minimum = double.PositiveInfinity;
		for (int index = 1; index < plan.Entries.Count; index++)
		{
			PipeSpacingPlanEntry left = plan.Entries[index - 1];
			PipeSpacingPlanEntry right = plan.Entries[index];
			Pipe leftPipe = document.GetElement(left.Cluster.SeedPipeId) as Pipe;
			Pipe rightPipe = document.GetElement(right.Cluster.SeedPipeId) as Pipe;
			Line leftLine = (leftPipe?.Location as LocationCurve)?.Curve as Line;
			Line rightLine = (rightPipe?.Location as LocationCurve)?.Curve as Line;
			if (leftLine == null || rightLine == null)
			{
				continue;
			}
			double centerDistance = PipeSpacingGeometry.Midpoint(rightLine).DotProduct(plan.SpacingAxis) -
			                        PipeSpacingGeometry.Midpoint(leftLine).DotProduct(plan.SpacingAxis);
			double gap = centerDistance - (left.EffectiveDiameter + right.EffectiveDiameter) / 2.0;
			minimum = Math.Min(minimum, gap);
		}
		return double.IsPositiveInfinity(minimum) ? 0.0 : minimum;
	}

	private static bool ApplyPreviewOverrides(
		View view,
		PipeSpacingPlan plan,
		IDictionary<long, OverrideGraphicSettings> originals,
		out string error)
	{
		error = string.Empty;
		try
		{
			foreach (PipeSpacingPlanEntry entry in plan.Entries)
			{
				Color color = entry.IsAnchor ? new Color(145, 145, 145) : new Color(0, 210, 120);
				OverrideGraphicSettings preview = new OverrideGraphicSettings();
				preview.SetProjectionLineColor(color);
				preview.SetProjectionLineWeight(entry.IsAnchor ? 4 : 6);
				foreach (ElementId id in entry.Cluster.AllOwnedElementIds)
				{
					if (!originals.ContainsKey(id.Value))
					{
						originals[id.Value] = view.GetElementOverrides(id);
					}
					view.SetElementOverrides(id, preview);
				}
			}
			return true;
		}
		catch (Exception exception)
		{
			error = "Không thể tô màu preview trong view hiện tại: " + exception.Message;
			return false;
		}
	}

	private static void RestorePreviewOverrides(
		View view,
		IReadOnlyDictionary<long, OverrideGraphicSettings> originals)
	{
		foreach (KeyValuePair<long, OverrideGraphicSettings> pair in originals)
		{
			view.SetElementOverrides(new ElementId(pair.Key), pair.Value);
		}
	}
}
