using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_CopyVG : SecuredExternalCommand
{
	private const string DialogTitle = "Copy VG";

	protected override void ExecuteCore()
	{
		try
		{
			int validOpenViewCount = UI_CopyVGWindow.CountEligibleOpenViews(base.UiDocument);
			if (validOpenViewCount < 2)
			{
				TaskDialog.Show("Copy VG", "Không tìm thấy đủ ít nhất 2 view hợp lệ đang mở.\n\nChỉ hỗ trợ view loại Plan/Section/Elevation (không phải template, không phải sheet).\nVui lòng mở thêm view hợp lệ rồi chạy lại lệnh.");
				return;
			}
			UI_CopyVGWindow window = new UI_CopyVGWindow(base.UiDocument);
			if (window.ShowDialog() != true || window.SourceViewId == null || window.TargetViewId == null)
			{
				return;
			}
			if (window.SourceViewId == window.TargetViewId)
			{
				TaskDialog.Show("Copy VG", "Source view và target view phải khác nhau.");
				return;
			}
			Element sourceElement = base.Document.GetElement(window.SourceViewId);
			Element targetElement = base.Document.GetElement(window.TargetViewId);
			if (!(sourceElement is View sourceView) || !(targetElement is View targetView))
			{
				TaskDialog.Show("Copy VG", "Không thể đọc source/target view. Vui lòng chọn lại.");
				return;
			}
			if (!UI_CopyVGWindow.IsEligibleView(sourceView) || !UI_CopyVGWindow.IsEligibleView(targetView))
			{
				TaskDialog.Show("Copy VG", "Source/target view không thuộc phạm vi hỗ trợ (Plan/Section/Elevation).");
				return;
			}
			if (sourceView.ViewType != targetView.ViewType)
			{
				TaskDialog.Show("Copy VG", $"Không thể copy giữa 2 loại view khác nhau.\nSource: {sourceView.ViewType}\nTarget: {targetView.ViewType}");
				return;
			}
			int okCount = 0;
			int failCount = 0;
			int skipCount = 0;
			List<string> report = new List<string>();
			using Transaction transaction = new Transaction(base.Document, "Copy VG");
			transaction.Start();
			try
			{
				CopyViewTemplate(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				CopyCategoryOverrides(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				CopyFilterOverrides(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				CopyImportedCategoryOverrides(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				CopyWorksetVisibility(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				CopyViewParameters(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
				if (okCount > 0)
				{
					transaction.Commit();
				}
				else
				{
					transaction.RollBack();
				}
			}
			catch
			{
				if (transaction.GetStatus() == TransactionStatus.Started)
				{
					transaction.RollBack();
				}
				throw;
			}
			TaskDialog.Show("Copy VG", "Source: " + sourceView.Name + "\nTarget: " + targetView.Name + "\n\n" + $"OK: {okCount}\n" + $"SKIP: {skipCount}\n" + $"FAIL: {failCount}\n\n" + string.Join("\n", report));
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Copy VG", "Đã xảy ra lỗi khi copy VG.\n\nLý do: " + ex2.Message);
		}
	}

	private static void CopyViewTemplate(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		try
		{
			targetView.ViewTemplateId = sourceView.ViewTemplateId;
			okCount++;
			report.Add($"OK: ViewTemplateId = {sourceView.ViewTemplateId.IntegerValue}");
		}
		catch (Exception ex)
		{
			failCount++;
			report.Add("FAIL: ViewTemplate - " + ex.Message);
		}
	}

	private static void CopyCategoryOverrides(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		int localOk = 0;
		int localSkip = 0;
		int localFail = 0;
		foreach (Category category in sourceView.Document.Settings.Categories)
		{
			if (category != null && !(category.Id == null))
			{
				try
				{
					OverrideGraphicSettings ogs = sourceView.GetCategoryOverrides(category.Id);
					targetView.SetCategoryOverrides(category.Id, ogs);
					localOk++;
				}
				catch
				{
					localSkip++;
				}
			}
		}
		okCount += localOk;
		skipCount += localSkip;
		failCount += localFail;
		report.Add($"OK: Category Overrides = {localOk}, SKIP = {localSkip}, FAIL = {localFail}");
	}

	private static void CopyImportedCategoryOverrides(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		ICollection<ElementId> importedCategoryIds = TryGetImportedCategoryIds(sourceView);
		if (importedCategoryIds == null)
		{
			skipCount++;
			report.Add("SKIP: Imported Category Overrides - Revit API hiện tại không hỗ trợ đọc imported categories.");
			return;
		}
		int localOk = 0;
		int localSkip = 0;
		foreach (ElementId categoryId in importedCategoryIds)
		{
			try
			{
				OverrideGraphicSettings ogs = sourceView.GetCategoryOverrides(categoryId);
				targetView.SetCategoryOverrides(categoryId, ogs);
				localOk++;
			}
			catch
			{
				localSkip++;
			}
		}
		okCount += localOk;
		skipCount += localSkip;
		report.Add($"OK: Imported Category Overrides = {localOk}, SKIP = {localSkip}, FAIL = 0");
	}

	private static ICollection<ElementId>? TryGetImportedCategoryIds(View view)
	{
		try
		{
			MethodInfo method = typeof(View).GetMethod("GetImportedCategories", Type.EmptyTypes);
			if (method == null)
			{
				return null;
			}
			return method.Invoke(view, null) as ICollection<ElementId>;
		}
		catch
		{
			return null;
		}
	}

	private static void CopyFilterOverrides(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		ICollection<ElementId> filterIds;
		try
		{
			filterIds = sourceView.GetFilters();
		}
		catch (Exception ex)
		{
			failCount++;
			report.Add("FAIL: Filter Overrides - " + ex.Message);
			return;
		}
		int localOk = 0;
		int localSkip = 0;
		int localFail = 0;
		foreach (ElementId filterId in filterIds)
		{
			try
			{
				OverrideGraphicSettings ogs = sourceView.GetFilterOverrides(filterId);
				bool isVisible = sourceView.GetFilterVisibility(filterId);
				try
				{
					targetView.AddFilter(filterId);
				}
				catch
				{
				}
				targetView.SetFilterOverrides(filterId, ogs);
				targetView.SetFilterVisibility(filterId, isVisible);
				localOk++;
			}
			catch (Exception ex2)
			{
				if (ex2 is Autodesk.Revit.Exceptions.ArgumentException)
				{
					localSkip++;
				}
				else
				{
					localFail++;
				}
			}
		}
		okCount += localOk;
		skipCount += localSkip;
		failCount += localFail;
		report.Add($"OK: Filter Overrides = {localOk}, SKIP = {localSkip}, FAIL = {localFail}");
	}

	private static void CopyWorksetVisibility(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		int localOk = 0;
		int localSkip = 0;
		FilteredWorksetCollector collector = new FilteredWorksetCollector(sourceView.Document).OfKind(WorksetKind.UserWorkset);
		foreach (Workset workset in collector)
		{
			try
			{
				WorksetVisibility visibility = sourceView.GetWorksetVisibility(workset.Id);
				targetView.SetWorksetVisibility(workset.Id, visibility);
				localOk++;
			}
			catch
			{
				localSkip++;
			}
		}
		okCount += localOk;
		skipCount += localSkip;
		report.Add($"OK: Workset Visibility = {localOk}, SKIP = {localSkip}, FAIL = 0");
	}

	private static void CopyViewParameters(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		CopyEnumParameter(sourceView, targetView, BuiltInParameter.VIEW_DISCIPLINE, "Discipline", report, ref okCount, ref skipCount, ref failCount);
		CopyEnumParameter(sourceView, targetView, BuiltInParameter.MODEL_GRAPHICS_STYLE, "Detail Level", report, ref okCount, ref skipCount, ref failCount);
		CopyEnumParameter(sourceView, targetView, BuiltInParameter.VIEW_PARTS_VISIBILITY, "Parts Visibility", report, ref okCount, ref skipCount, ref failCount);
		CopyViewScale(sourceView, targetView, report, ref okCount, ref skipCount, ref failCount);
		if (sourceView is ViewPlan sourcePlan && targetView is ViewPlan targetPlan)
		{
			CopyPlanViewRangeByTargetAssociatedLevel(sourcePlan, targetPlan, report, ref okCount, ref skipCount, ref failCount);
			return;
		}
		skipCount++;
		report.Add("SKIP: ViewRange - Chỉ áp dụng cho ViewPlan.");
	}

	private static void CopyViewScale(View sourceView, View targetView, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		if (sourceView.Scale <= 0)
		{
			skipCount++;
			report.Add("SKIP: Scale - Source scale không hợp lệ.");
			return;
		}
		try
		{
			targetView.Scale = sourceView.Scale;
			okCount++;
			report.Add($"OK: Scale = 1:{sourceView.Scale}");
		}
		catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
		{
			skipCount++;
			report.Add("SKIP: Scale - " + ex.Message);
		}
		catch (Autodesk.Revit.Exceptions.ArgumentException ex2)
		{
			skipCount++;
			report.Add("SKIP: Scale - " + ex2.Message);
		}
		catch (Exception ex3)
		{
			failCount++;
			report.Add("FAIL: Scale - " + ex3.Message);
		}
	}

	private static void CopyPlanViewRangeByTargetAssociatedLevel(ViewPlan sourcePlan, ViewPlan targetPlan, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		try
		{
			PlanViewRange sourceRange = sourcePlan.GetViewRange();
			PlanViewRange targetRange = targetPlan.GetViewRange();
			ElementId sourceAssociatedLevelId = TryGetAssociatedLevelId(sourcePlan);
			ElementId targetAssociatedLevelId = TryGetAssociatedLevelId(targetPlan);
			if (sourceAssociatedLevelId == null || targetAssociatedLevelId == null)
			{
				skipCount++;
				report.Add("SKIP: ViewRange - Không xác định được Associated Level của source/target.");
				return;
			}
			List<Level> orderedLevels = (from Level level in new FilteredElementCollector(sourcePlan.Document).OfClass(typeof(Level))
				orderby level.Elevation
				select level).ThenBy((Level level) => level.Name, StringComparer.Ordinal).ToList();
			if (orderedLevels.Count == 0)
			{
				skipCount++;
				report.Add("SKIP: ViewRange - Không tìm thấy level trong model.");
				return;
			}
			Dictionary<long, int> levelIndexById = orderedLevels.Select((Level level, int index) => new { level.Id, index }).ToDictionary(x => x.Id.Value, x => x.index);
			if (!levelIndexById.TryGetValue(sourceAssociatedLevelId.Value, out var sourceAssociatedIndex) || !levelIndexById.TryGetValue(targetAssociatedLevelId.Value, out var targetAssociatedIndex))
			{
				skipCount++;
				report.Add("SKIP: ViewRange - Associated Level không tồn tại trong danh sách level chuẩn.");
				return;
			}
			int localSkip = 0;
			foreach (PlanViewPlane plane in GetSupportedPlanes())
			{
				ElementId sourcePlaneLevelId = sourceRange.GetLevelId(plane);
				double sourceOffset = sourceRange.GetOffset(plane);
				if (!levelIndexById.TryGetValue(sourcePlaneLevelId.Value, out var sourcePlaneIndex))
				{
					localSkip++;
					report.Add("SKIP: ViewRange Plane " + GetPlaneLabel(plane) + " - không map được level source, giữ level target hiện tại.");
					targetRange.SetOffset(plane, sourceOffset);
					continue;
				}
				int delta = sourcePlaneIndex - sourceAssociatedIndex;
				int mappedTargetIndex = targetAssociatedIndex + delta;
				int clampedTargetIndex = Math.Max(0, Math.Min(orderedLevels.Count - 1, mappedTargetIndex));
				if (mappedTargetIndex != clampedTargetIndex)
				{
					localSkip++;
					report.Add("SKIP: ViewRange Plane " + GetPlaneLabel(plane) + " - level vượt biên, đã clamp về level gần nhất.");
				}
				Level mappedTargetLevel = orderedLevels[clampedTargetIndex];
				targetRange.SetLevelId(plane, mappedTargetLevel.Id);
				targetRange.SetOffset(plane, sourceOffset);
			}
			targetPlan.SetViewRange(targetRange);
			okCount++;
			report.Add((localSkip == 0) ? "OK: ViewRange" : "OK: ViewRange (có plane cần clamp/fallback).");
			skipCount += localSkip;
		}
		catch (Exception ex)
		{
			failCount++;
			report.Add("FAIL: ViewRange - " + ex.Message);
		}
	}

	private static IEnumerable<PlanViewPlane> GetSupportedPlanes()
	{
		yield return PlanViewPlane.TopClipPlane;
		yield return PlanViewPlane.CutPlane;
		yield return PlanViewPlane.BottomClipPlane;
		yield return PlanViewPlane.ViewDepthPlane;
	}

	private static string GetPlaneLabel(PlanViewPlane plane)
	{
		if (1 == 0)
		{
		}
		string result = plane switch
		{
			PlanViewPlane.TopClipPlane => "TopClipPlane", 
			PlanViewPlane.CutPlane => "CutPlane", 
			PlanViewPlane.BottomClipPlane => "BottomClipPlane", 
			PlanViewPlane.ViewDepthPlane => "ViewDepthPlane", 
			_ => plane.ToString(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static ElementId? TryGetAssociatedLevelId(ViewPlan viewPlan)
	{
		if (viewPlan.GenLevel != null)
		{
			return viewPlan.GenLevel.Id;
		}
		Parameter associatedLevelParam = ((Element)viewPlan).get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
		if (associatedLevelParam == null)
		{
			return null;
		}
		ElementId levelId = associatedLevelParam.AsElementId();
		return (levelId == ElementId.InvalidElementId) ? null : levelId;
	}

	private static void CopyEnumParameter(View sourceView, View targetView, BuiltInParameter builtInParameter, string label, ICollection<string> report, ref int okCount, ref int skipCount, ref int failCount)
	{
		Parameter sourceParam = ((Element)sourceView).get_Parameter(builtInParameter);
		Parameter targetParam = ((Element)targetView).get_Parameter(builtInParameter);
		if (sourceParam == null || targetParam == null)
		{
			skipCount++;
			report.Add("SKIP: " + label + " - Không tìm thấy parameter.");
			return;
		}
		if (targetParam.IsReadOnly)
		{
			skipCount++;
			report.Add("SKIP: " + label + " - Parameter trên target là read-only.");
			return;
		}
		try
		{
			targetParam.Set(sourceParam.AsInteger());
			okCount++;
			report.Add("OK: " + label + " = " + (sourceParam.AsValueString() ?? sourceParam.AsInteger().ToString()));
		}
		catch (Exception ex)
		{
			failCount++;
			report.Add("FAIL: " + label + " - " + ex.Message);
		}
	}
}
