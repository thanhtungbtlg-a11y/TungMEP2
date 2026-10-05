using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.State;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal static class PipeSpacingRunner
{
	private sealed class StraightPipeSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return element is Pipe { Location: LocationCurve { Curve: Line } };
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return false;
		}
	}

	internal static void Run(UIDocument uiDocument, Document document)
	{
		if (uiDocument == null || document == null || document.IsFamilyDocument || document.IsReadOnly)
		{
			TaskDialog.Show("PipeSpacing", "Lệnh chỉ chạy trong project Revit có thể chỉnh sửa.");
			return;
		}

		List<ElementId> selectedIds = uiDocument.Selection.GetElementIds()
			.Select(document.GetElement)
			.OfType<Pipe>()
			.Where(pipe => pipe.Location is LocationCurve { Curve: Line })
			.Select(pipe => pipe.Id)
			.GroupBy(id => id.Value)
			.Select(group => group.First())
			.ToList();
		if (selectedIds.Count < 2)
		{
			// Pipe spacing selection prompt.
			IList<Reference> picked = uiDocument.Selection.PickObjects(
				ObjectType.Element,
				new StraightPipeSelectionFilter(),
				"Chọn các ống song song cần dàn khoảng cách");
			selectedIds = picked
				.Select(reference => reference.ElementId)
				.GroupBy(id => id.Value)
				.Select(group => group.First())
				.ToList();
		}
		if (selectedIds.Count < 2)
		{
			TaskDialog.Show("PipeSpacing", "Vui lòng chọn ít nhất hai Pipe thẳng song song.");
			return;
		}

		PipeSpacingSettings initialSettings = PipeSpacingConfigStore.Load(document);
		PipeSpacingDialogPreview BuildPreview(PipeSpacingSettings settings)
		{
			if (!PipeSpacingAnalysisService.TryCreatePlan(
					uiDocument,
					selectedIds,
					settings,
					out PipeSpacingPlan previewPlan,
					out PipeSpacingAnalysisReport previewReport,
					out string previewError))
			{
				throw new InvalidOperationException(previewError);
			}
			PipeSpacingPlanEntry anchor = previewPlan.Entries.First(entry => entry.IsAnchor);
			return new PipeSpacingDialogPreview(
				previewReport.Rows,
				ToMillimetres(previewPlan.MinimumClearGap),
				anchor.Cluster.SeedPipeId.Value,
				previewReport.Warnings);
		}

		UI_PipeSpacingWindow window = new UI_PipeSpacingWindow(initialSettings, BuildPreview);
		new WindowInteropHelper(window)
		{
			Owner = uiDocument.Application.MainWindowHandle
		};
		if (window.ShowDialog() != true)
		{
			return;
		}

		PipeSpacingSettings finalSettings = window.SelectedSettings.Normalize();
		PipeSpacingConfigStore.Save(document, finalSettings);
		if (!PipeSpacingAnalysisService.TryCreatePlan(
				uiDocument,
				selectedIds,
				finalSettings,
				out PipeSpacingPlan finalPlan,
				out PipeSpacingAnalysisReport finalAnalysis,
				out string analysisError))
		{
			TaskDialog.Show("PipeSpacing", analysisError);
			return;
		}

		if (!PipeSpacingExecutor.TryApply(
				uiDocument,
				finalPlan,
				window.PreviewBeforeApply,
				out PipeSpacingApplyReport applyReport,
				out string applyError))
		{
			TaskDialog.Show("PipeSpacing", applyError);
			return;
		}
		if (applyReport.Cancelled)
		{
			return;
		}

		int skipped = finalAnalysis.Rows.Count(row =>
			!string.Equals(row.Status, "Ready", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(row.Status, "Anchor", StringComparison.OrdinalIgnoreCase));
		string details =
			$"Cụm đã dịch: {applyReport.MovedClusterCount}\n" +
			$"Đoạn Pipe biên đã kéo: {applyReport.StretchedBoundaryCount}\n" +
			$"Khoảng hở nhỏ nhất: {ToMillimetres(applyReport.MinimumClearGap):0.##} mm\n" +
			$"Bỏ qua: {skipped}\n\n" +
			finalPlan.AnchorReason;
		List<string> warnings = finalAnalysis.Warnings.Concat(applyReport.Warnings).ToList();
		if (warnings.Count > 0)
		{
			details += "\n\nCảnh báo:\n- " + string.Join("\n- ", warnings);
		}
		TaskDialog.Show("TungMEP2 - PipeSpacing", details);
	}

	private static double ToMillimetres(double internalValue)
	{
		return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters);
	}
}
