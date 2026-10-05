using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceRunner
{
	private sealed class FitSpaceSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return IsAllowedElement(element);
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
			TaskDialog.Show("FitSpace", "Lệnh chỉ chạy trong project Revit có thể chỉnh sửa.");
			return;
		}

		List<ElementId> selectedIds = uiDocument.Selection.GetElementIds()
			.Select(document.GetElement)
			.Where(IsAllowedElement)
			.Select(element => element.Id)
			.GroupBy(id => id.Value)
			.Select(group => group.First())
			.ToList();

		FitSpacePlan plan = null;
		FitSpaceAnalysisReport analysisReport = null;
		string analysisError = string.Empty;
		bool hasPlan = selectedIds.Count > 0 && FitSpaceAnalysisService.TryCreatePlan(
			uiDocument,
			selectedIds,
			out plan,
			out analysisReport,
			out analysisError);
		if (!hasPlan)
		{
			IList<Reference> picked = uiDocument.Selection.PickObjects(
				ObjectType.Element,
				new FitSpaceSelectionFilter(),
				"Chọn Pipe giữa hoặc hai elbow cần chuẩn hóa khoảng cách");
			selectedIds = picked
				.Select(reference => reference.ElementId)
				.GroupBy(id => id.Value)
				.Select(group => group.First())
				.ToList();

			hasPlan = FitSpaceAnalysisService.TryCreatePlan(
				uiDocument,
				selectedIds,
				out plan,
				out analysisReport,
				out analysisError);
		}

		if (!hasPlan)
		{
			TaskDialog.Show("FitSpace", analysisError);
			return;
		}

		bool success = FitSpaceExecutor.TryExecute(
			uiDocument,
			plan,
			out FitSpaceExecutionReport executionReport,
			out string executionError);
		int totalSkipped = analysisReport.Excluded.Count + executionReport.SkippedCount;
		string message =
			$"Đã chỉnh: {executionReport.AdjustedCount}\n" +
			$"Đã đúng khoảng cách: {executionReport.AlreadyNormalizedCount}\n" +
			$"Bỏ qua: {totalSkipped}\n" +
			$"Rollback: {executionReport.RolledBackCount}";

		List<string> reasons = new List<string>();
		if (analysisReport.Excluded.Count > 0)
		{
			reasons.Add("Phân tích: " + analysisReport.Excluded[0].Reason);
		}
		if (executionReport.Skipped.Count > 0)
		{
			reasons.Add("Bỏ qua: " + executionReport.Skipped[0].Reason);
		}
		if (executionReport.RolledBack.Count > 0)
		{
			reasons.Add("Rollback: " + executionReport.RolledBack[0].Reason);
		}
		if (!success && !string.IsNullOrWhiteSpace(executionError))
		{
			reasons.Add("Lỗi: " + executionError);
		}
		if (reasons.Count > 0)
		{
			message += "\n\n" + string.Join("\n", reasons);
		}
		TaskDialog.Show("TungMEP2 - FitSpace", message);
	}

	private static bool IsAllowedElement(Element element)
	{
		return element is Pipe || element is FamilyInstance &&
			element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting;
	}
}
