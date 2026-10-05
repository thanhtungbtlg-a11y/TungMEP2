using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Common;

internal static class SelectElementsByLevelRunner
{
	private sealed class RelevelCandidate
	{
		internal Element Element { get; }

		internal Parameter? Parameter { get; }

		internal ElementId CurrentLevelId { get; }

		internal ElementId TargetLevelId { get; }

		internal bool CanApply { get; }

		internal string FailureReason { get; }

		internal RelevelCandidate(Element element, Parameter? parameter, ElementId currentLevelId, ElementId targetLevelId, bool canApply, string failureReason)
		{
			Element = element;
			Parameter = parameter;
			CurrentLevelId = currentLevelId;
			TargetLevelId = targetLevelId;
			CanApply = canApply;
			FailureReason = failureReason ?? string.Empty;
		}

		internal RelevelFailureRecord ToFailureRecord()
		{
			Document document = Element.Document;
			return new RelevelFailureRecord(Element.Id.IntegerValue, Element.Category?.Name ?? string.Empty, Element.Name, ResolveLevelName(document, CurrentLevelId), ResolveLevelName(document, TargetLevelId), FailureReason);
		}
	}

	private sealed class RelevelFailureRecord
	{
		internal int ElementId { get; }

		internal string Category { get; }

		internal string Name { get; }

		internal string CurrentLevel { get; }

		internal string TargetLevel { get; }

		internal string FailureReason { get; }

		internal RelevelFailureRecord(int elementId, string category, string name, string currentLevel, string targetLevel, string failureReason)
		{
			ElementId = elementId;
			Category = category ?? string.Empty;
			Name = name ?? string.Empty;
			CurrentLevel = currentLevel ?? string.Empty;
			TargetLevel = targetLevel ?? string.Empty;
			FailureReason = failureReason ?? string.Empty;
		}
	}

	private readonly struct ScopeSelectionResult
	{
		internal SelectionScope Scope { get; }

		internal bool RunCheckOnly { get; }

		internal ScopeSelectionResult(SelectionScope scope, bool runCheckOnly)
		{
			Scope = scope;
			RunCheckOnly = runCheckOnly;
		}
	}

	private const string DialogTitle = "Select Elements By Level";

	private static readonly BuiltInParameter[] CandidateLevelParameters = new BuiltInParameter[4]
	{
		BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
		BuiltInParameter.FAMILY_LEVEL_PARAM,
		BuiltInParameter.SCHEDULE_LEVEL_PARAM,
		BuiltInParameter.RBS_START_LEVEL_PARAM
	};

	internal static void Run(UIDocument uiDocument, Document document)
	{
		try
		{
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
			ScopeSelectionResult scopeResult = PromptScope();
			if (scopeResult.Scope == SelectionScope.None)
			{
				return;
			}
			if (scopeResult.Scope == SelectionScope.ActiveView && document.ActiveView == null)
			{
				TaskDialog.Show("Select Elements By Level", "Không tìm thấy Active View hợp lệ.");
				return;
			}
			if (scopeResult.RunCheckOnly)
			{
				ShowActiveViewLevelUsage(document);
				return;
			}
			List<Level> levels = (from Level level in new FilteredElementCollector(document).OfClass(typeof(Level))
				orderby level.Elevation
				select level).ThenBy((Level level) => level.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			if (levels.Count == 0)
			{
				TaskDialog.Show("Select Elements By Level", "Project does not contain any level.");
				return;
			}
			List<LevelOption> levelOptions = levels.Select((Level level) => new LevelOption(level.Id, level.Name, level.Elevation)).ToList();
			ElementId sourceLevelId = PromptLevel(levelOptions, "Select Source Level", "Chọn level nguồn để lọc danh sách phần tử.", "Filter");
			if (!IsValid(sourceLevelId))
			{
				return;
			}
			List<ElementId> matchedElementIds = (from element in GetCollectorByScope(document, scopeResult.Scope).WhereElementIsNotElementType()
				where element != null && element.Id != ElementId.InvalidElementId
				where ResolveLevelId(element) == sourceLevelId
				select element.Id).Distinct().ToList();
			if (matchedElementIds.Count == 0)
			{
				TaskDialog.Show("Select Elements By Level", "No elements found for the selected source level.");
				return;
			}
			uiDocument.Selection.SetElementIds(matchedElementIds);
			List<CategoryOption> categories = BuildCategoryOptions(document, matchedElementIds);
			UI_SelectElementsByLevelActionWindow actionWindow = new UI_SelectElementsByLevelActionWindow(matchedElementIds.Count, categories);
			if (actionWindow.ShowDialog() != true)
			{
				return;
			}
			switch (actionWindow.SelectedAction)
			{
			case SelectElementsByLevelAction.Selection:
				uiDocument.Selection.SetElementIds(matchedElementIds);
				break;
			case SelectElementsByLevelAction.FilterByCategory:
				ApplyCategoryFilter(uiDocument, document, matchedElementIds, actionWindow.SelectedCategoryNames);
				break;
			case SelectElementsByLevelAction.Relevel:
			{
				IReadOnlyList<ElementId> relevelScopeElementIds = ((actionWindow.SelectedCategoryNames.Count == 0) ? ((IReadOnlyList<ElementId>)matchedElementIds) : ((IReadOnlyList<ElementId>)GetCategoryFilteredElementIds(document, matchedElementIds, actionWindow.SelectedCategoryNames)));
				if (relevelScopeElementIds.Count == 0)
				{
					TaskDialog.Show("Select Elements By Level", "Không có phần tử thuộc category đã chọn.");
				}
				else
				{
					RunRelevelFlow(uiDocument, document, relevelScopeElementIds, sourceLevelId, levelOptions);
				}
				break;
			}
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Select Elements By Level", ex2.ToString());
		}
	}

	private static FilteredElementCollector GetCollectorByScope(Document document, SelectionScope scope)
	{
		if (scope == SelectionScope.ActiveView)
		{
			return new FilteredElementCollector(document, document.ActiveView.Id);
		}
		return new FilteredElementCollector(document);
	}

	private static ScopeSelectionResult PromptScope()
	{
		UI_SelectElementsByLevelScopeWindow picker = new UI_SelectElementsByLevelScopeWindow();
		if (picker.ShowDialog() != true)
		{
			return new ScopeSelectionResult(SelectionScope.None, runCheckOnly: false);
		}
		return new ScopeSelectionResult(picker.SelectedScope, picker.RunCheckOnly);
	}

	private static void ShowActiveViewLevelUsage(Document document)
	{
		if (document.ActiveView == null)
		{
			TaskDialog.Show("Select Elements By Level", "Không tìm thấy Active View hợp lệ.");
			return;
		}
		List<Element> elements = (from element in new FilteredElementCollector(document, document.ActiveView.Id).WhereElementIsNotElementType()
			where element != null && element.Id != ElementId.InvalidElementId
			select element).ToList();
		List<IGrouping<string, Element>> levelGroups = (from @group in elements.GroupBy(delegate(Element element)
			{
				ElementId levelId = ResolveLevelId(element);
				string text = ResolveLevelName(document, levelId);
				return string.IsNullOrWhiteSpace(text) ? "(No Level)" : text;
			})
			orderby @group.Count() descending
			select @group).ThenBy((IGrouping<string, Element> group) => group.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
		string details = ((levelGroups.Count == 0) ? "Không có level nào được resolve từ phần tử trong Active View." : string.Join("\n", levelGroups.Select((IGrouping<string, Element> group) => $"{group.Key}: {group.Count()}")));
		string message = "Active View: " + document.ActiveView.Name + $"\nTổng element instance: {elements.Count}" + $"\nSố level tìm thấy: {levelGroups.Count}" + "\n\nDanh sách level:\n" + details;
		TaskDialog.Show("Select Elements By Level", message);
	}

	private static void ApplyCategoryFilter(UIDocument uiDocument, Document document, IReadOnlyList<ElementId> matchedElementIds, IReadOnlyList<string> selectedCategoryNames)
	{
		if (selectedCategoryNames == null || selectedCategoryNames.Count == 0)
		{
			TaskDialog.Show("Select Elements By Level", "Category chưa hợp lệ.");
			return;
		}
		HashSet<string> selectedSet = new HashSet<string>(selectedCategoryNames.Where((string name) => !string.IsNullOrWhiteSpace(name)), StringComparer.CurrentCultureIgnoreCase);
		if (selectedSet.Count == 0)
		{
			TaskDialog.Show("Select Elements By Level", "Category chưa hợp lệ.");
			return;
		}
		List<ElementId> filtered = GetCategoryFilteredElementIds(document, matchedElementIds, selectedSet);
		if (filtered.Count == 0)
		{
			TaskDialog.Show("Select Elements By Level", "Không có phần tử thuộc category đã chọn.");
		}
		else
		{
			uiDocument.Selection.SetElementIds(filtered);
		}
	}

	private static List<ElementId> GetCategoryFilteredElementIds(Document document, IReadOnlyList<ElementId> matchedElementIds, IEnumerable<string> selectedCategoryNames)
	{
		HashSet<string> selectedSet = new HashSet<string>(selectedCategoryNames.Where((string name) => !string.IsNullOrWhiteSpace(name)), StringComparer.CurrentCultureIgnoreCase);
		return (from element in matchedElementIds.Select(document.GetElement)
			where element != null
			where selectedSet.Contains(GetCategoryName(element))
			select element.Id).ToList();
	}

	private static void RunRelevelFlow(UIDocument uiDocument, Document document, IReadOnlyList<ElementId> matchedElementIds, ElementId sourceLevelId, IReadOnlyList<LevelOption> levelOptions)
	{
		ElementId targetLevelId = PromptLevel(levelOptions, "Select Target Level", "Chọn level đích để đổi cho danh sách phần tử đã lọc.", "Re-Level");
		if (!IsValid(targetLevelId))
		{
			return;
		}
		if (targetLevelId == sourceLevelId)
		{
			TaskDialog.Show("Select Elements By Level", "Source level và target level đang trùng nhau.");
			return;
		}
		List<RelevelCandidate> candidates = BuildRelevelCandidates(document, matchedElementIds, targetLevelId);
		List<RelevelCandidate> eligible = candidates.Where((RelevelCandidate item) => item.CanApply).ToList();
		List<RelevelFailureRecord> failures = (from item in candidates
			where !item.CanApply
			select item.ToFailureRecord()).ToList();
		string reportPath = null;
		if (failures.Count > 0)
		{
			reportPath = ExportFailureReport(failures);
		}
		if (eligible.Count == 0)
		{
			TaskDialog.Show("Select Elements By Level", BuildSummaryMessage(matchedElementIds.Count, 0, failures.Count, reportPath));
			return;
		}
		TaskDialogResult confirmResult = TaskDialog.Show("Select Elements By Level", BuildSummaryMessage(matchedElementIds.Count, eligible.Count, failures.Count, reportPath) + "\n\nĐồng ý đổi level cho các phần tử hợp lệ?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
		if (confirmResult == TaskDialogResult.Yes)
		{
			ApplyRelevel(document, eligible, targetLevelId);
			uiDocument.Selection.SetElementIds(matchedElementIds.ToList());
			TaskDialog.Show("Select Elements By Level", $"Đã đổi level thành công cho {eligible.Count}/{matchedElementIds.Count} phần tử." + ((failures.Count > 0) ? ("\nChi tiết lỗi: " + reportPath) : string.Empty));
		}
	}

	private static List<CategoryOption> BuildCategoryOptions(Document document, IReadOnlyList<ElementId> matchedElementIds)
	{
		return (from @group in (from @group in (from element in matchedElementIds.Select(document.GetElement)
					where element != null
					select element).GroupBy(GetCategoryName)
				orderby @group.Count() descending
				select @group).ThenBy((IGrouping<string, Element> @group) => @group.Key, StringComparer.CurrentCultureIgnoreCase)
			select new CategoryOption(@group.Key, @group.Count())).ToList();
	}

	private static string GetCategoryName(Element element)
	{
		string categoryName = element.Category?.Name ?? string.Empty;
		return string.IsNullOrWhiteSpace(categoryName) ? "(No Category)" : categoryName.Trim();
	}

	private static ElementId PromptLevel(IEnumerable<LevelOption> options, string windowTitle, string promptText, string selectButtonText)
	{
		UI_SelectLevelWindow picker = new UI_SelectLevelWindow(options, windowTitle, promptText, selectButtonText);
		if (picker.ShowDialog() != true)
		{
			return ElementId.InvalidElementId;
		}
		return picker.SelectedLevelId;
	}

	private static List<RelevelCandidate> BuildRelevelCandidates(Document document, IEnumerable<ElementId> elementIds, ElementId targetLevelId)
	{
		List<RelevelCandidate> candidates = new List<RelevelCandidate>();
		using Transaction tx = new Transaction(document, "Validate Relevel Candidates");
		tx.Start();
		foreach (ElementId elementId in elementIds)
		{
			Element element = document.GetElement(elementId);
			if (element != null)
			{
				Parameter parameter = FindWritableLevelParameter(element);
				if (parameter == null)
				{
					candidates.Add(new RelevelCandidate(element, parameter, ResolveLevelId(element), targetLevelId, canApply: false, "No writable level parameter."));
					continue;
				}
				string reason;
				bool canApply = CanSetLevelParameter(parameter, targetLevelId, out reason);
				candidates.Add(new RelevelCandidate(element, parameter, ResolveLevelId(element), targetLevelId, canApply, reason));
			}
		}
		tx.RollBack();
		return candidates;
	}

	private static Parameter? FindWritableLevelParameter(Element element)
	{
		BuiltInParameter[] candidateLevelParameters = CandidateLevelParameters;
		foreach (BuiltInParameter parameterId in candidateLevelParameters)
		{
			Parameter parameter = element.get_Parameter(parameterId);
			if (parameter != null && parameter.StorageType == StorageType.ElementId && !parameter.IsReadOnly)
			{
				return parameter;
			}
		}
		return null;
	}

	private static bool CanSetLevelParameter(Parameter parameter, ElementId targetLevelId, out string reason)
	{
		reason = string.Empty;
		try
		{
			using SubTransaction subTx = new SubTransaction(parameter.Element.Document);
			subTx.Start();
			bool setResult = parameter.Set(targetLevelId);
			subTx.RollBack();
			if (!setResult)
			{
				reason = "Parameter.Set returned false.";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			return false;
		}
	}

	private static void ApplyRelevel(Document document, IEnumerable<RelevelCandidate> eligible, ElementId targetLevelId)
	{
		using Transaction tx = new Transaction(document, "Relevel Elements By Source Level");
		tx.Start();
		foreach (RelevelCandidate candidate in eligible)
		{
			if (candidate.Parameter != null)
			{
				candidate.Parameter.Set(targetLevelId);
			}
		}
		tx.Commit();
	}

	private static string? ExportFailureReport(IReadOnlyList<RelevelFailureRecord> failures)
	{
		if (failures.Count == 0)
		{
			return null;
		}
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		if (string.IsNullOrWhiteSpace(desktop))
		{
			desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		string filePath = Path.Combine(desktop, $"VP_RevitAPI_Relevel_Failures_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
		using ExcelPackage package = new ExcelPackage();
		ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Failures");
		sheet.Cells[1, 1].Value = "ElementId";
		sheet.Cells[1, 2].Value = "Category";
		sheet.Cells[1, 3].Value = "Name";
		sheet.Cells[1, 4].Value = "CurrentLevel";
		sheet.Cells[1, 5].Value = "TargetLevel";
		sheet.Cells[1, 6].Value = "FailureReason";
		for (int i = 0; i < failures.Count; i++)
		{
			RelevelFailureRecord record = failures[i];
			int row = i + 2;
			sheet.Cells[row, 1].Value = record.ElementId;
			sheet.Cells[row, 2].Value = record.Category;
			sheet.Cells[row, 3].Value = record.Name;
			sheet.Cells[row, 4].Value = record.CurrentLevel;
			sheet.Cells[row, 5].Value = record.TargetLevel;
			sheet.Cells[row, 6].Value = record.FailureReason;
		}
		sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
		package.SaveAs(new FileInfo(filePath));
		return filePath;
	}

	private static string BuildSummaryMessage(int total, int eligible, int failed, string? reportPath)
	{
		return $"Tổng phần tử match: {total}" + $"\nĐổi được: {eligible}" + $"\nLỗi: {failed}" + (string.IsNullOrWhiteSpace(reportPath) ? string.Empty : ("\nExcel lỗi: " + reportPath));
	}

	private static ElementId ResolveLevelId(Element element)
	{
		ElementId levelId = element.LevelId;
		if (IsValid(levelId))
		{
			return levelId;
		}
		BuiltInParameter[] candidateLevelParameters = CandidateLevelParameters;
		foreach (BuiltInParameter parameterId in candidateLevelParameters)
		{
			Parameter parameter = element.get_Parameter(parameterId);
			if (parameter != null && parameter.StorageType == StorageType.ElementId)
			{
				ElementId candidateLevelId = parameter.AsElementId();
				if (IsValid(candidateLevelId))
				{
					return candidateLevelId;
				}
			}
		}
		return ElementId.InvalidElementId;
	}

	private static string ResolveLevelName(Document document, ElementId levelId)
	{
		if (!IsValid(levelId))
		{
			return string.Empty;
		}
		return ((document.GetElement(levelId) is Level level) ? level.Name : null) ?? string.Empty;
	}

	private static bool IsValid(ElementId elementId)
	{
		return elementId != null && elementId != ElementId.InvalidElementId && elementId.IntegerValue > 0;
	}
}
