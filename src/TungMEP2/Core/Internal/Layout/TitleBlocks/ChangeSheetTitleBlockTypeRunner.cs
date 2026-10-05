using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper.Sheets;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Layout.TitleBlocks;

internal static class ChangeSheetTitleBlockTypeRunner
{
	private sealed class SheetTitleBlockItem
	{
		public ViewSheet Sheet { get; }

		public FamilyInstance TitleBlock { get; }

		public SheetTitleBlockItem(ViewSheet sheet, FamilyInstance titleBlock)
		{
			Sheet = sheet;
			TitleBlock = titleBlock;
		}
	}

	public static void Run(UIDocument uiDocument, Document document)
	{
		List<ViewSheet> sheets = TitleBlockSelectionUtilities.GetSheetsFromSelectedSchedules(uiDocument, document);
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong selection.");
		}
		List<SheetTitleBlockItem> sheetTitleBlocks = CollectSingleTitleBlockPerSheet(document, sheets);
		List<string> usedTypeSummaryLines = BuildUsedTypeSummaryLines(document, sheetTitleBlocks);
		List<TitleBlockFamilyOption> families = BuildTitleBlockFamilyOptions(document);
		if (families.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy TitleBlock type nào trong project.");
		}
		ElementId currentTypeId = sheetTitleBlocks[0].TitleBlock.GetTypeId();
		UI_ChangeSheetTitleBlockTypeWindow window = new UI_ChangeSheetTitleBlockTypeWindow(families, currentTypeId, sheets.Count, usedTypeSummaryLines);
		if (window.ShowDialog() != true)
		{
			throw new OperationCanceledException();
		}
		if (window.SelectedTypeId == ElementId.InvalidElementId)
		{
			throw new InvalidOperationException("Chưa chọn type TitleBlock hợp lệ.");
		}
		int changedCount = ApplyTitleBlockType(document, sheetTitleBlocks, window.SelectedTypeId);
		string selectedLabel = GetTitleBlockTypeLabel(document, window.SelectedTypeId);
		TaskDialog.Show("ChangeSheetTitleBlockType", $"Đã xử lý {sheetTitleBlocks.Count} sheet.\nĐổi type thành công: {changedCount}\nType: {selectedLabel}");
	}

	private static List<SheetTitleBlockItem> CollectSingleTitleBlockPerSheet(Document document, IEnumerable<ViewSheet> sheets)
	{
		List<SheetTitleBlockItem> results = new List<SheetTitleBlockItem>();
		List<string> errors = new List<string>();
		foreach (ViewSheet sheet in sheets)
		{
			List<FamilyInstance> titleBlocks = new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>()
				.ToList();
			if (titleBlocks.Count == 1)
			{
				results.Add(new SheetTitleBlockItem(sheet, titleBlocks[0]));
				continue;
			}
			string sheetLabel = sheet.SheetNumber + " - " + sheet.Name;
			if (titleBlocks.Count == 0)
			{
				errors.Add("- " + sheetLabel + ": không có titleblock.");
			}
			else
			{
				errors.Add($"- {sheetLabel}: có {titleBlocks.Count} titleblock (yêu cầu đúng 1).");
			}
		}
		if (errors.Count > 0)
		{
			throw new InvalidOperationException("Không thể đổi type TitleBlock vì có sheet không hợp lệ:\n" + string.Join("\n", errors));
		}
		return results;
	}

	private static List<TitleBlockFamilyOption> BuildTitleBlockFamilyOptions(Document document)
	{
		List<FamilySymbol> symbols = new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>()
			.ToList();
		return symbols.GroupBy<FamilySymbol, string>(GetFamilyName, StringComparer.OrdinalIgnoreCase).Select(delegate(IGrouping<string, FamilySymbol> group)
		{
			List<TitleBlockTypeOption> types = (from symbol in @group.OrderBy((FamilySymbol symbol) => symbol.Name, StringComparer.OrdinalIgnoreCase).ThenBy((FamilySymbol symbol) => symbol.Id.Value)
				select new TitleBlockTypeOption(symbol.Id, string.IsNullOrWhiteSpace(symbol.Name) ? "(Không rõ Type)" : symbol.Name.Trim())).ToList();
			return new TitleBlockFamilyOption(group.Key, types);
		}).OrderBy<TitleBlockFamilyOption, string>((TitleBlockFamilyOption option) => option.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<string> BuildUsedTypeSummaryLines(Document document, IReadOnlyList<SheetTitleBlockItem> sheetTitleBlocks)
	{
		return (from item in (from item in (from item in sheetTitleBlocks
					group item by item.TitleBlock.GetTypeId()).Select(delegate(IGrouping<ElementId, SheetTitleBlockItem> @group)
				{
					string titleBlockTypeLabel = GetTitleBlockTypeLabel(document, @group.Key);
					int item = @group.Count();
					return (label: titleBlockTypeLabel, count: item);
				})
				orderby item.count descending
				select item).ThenBy(((string label, int count) item) => item.label, StringComparer.OrdinalIgnoreCase)
			select $"{item.label}: {item.count}").ToList();
	}

	private static int ApplyTitleBlockType(Document document, IEnumerable<SheetTitleBlockItem> sheetTitleBlocks, ElementId targetTypeId)
	{
		int changedCount = 0;
		using Transaction transaction = new Transaction(document, "Change Sheet TitleBlock Type");
		transaction.Start();
		foreach (SheetTitleBlockItem item in sheetTitleBlocks)
		{
			FamilyInstance titleBlock = item.TitleBlock;
			if (!(titleBlock.GetTypeId() == targetTypeId))
			{
				titleBlock.ChangeTypeId(targetTypeId);
				changedCount++;
			}
		}
		transaction.Commit();
		return changedCount;
	}

	private static string GetFamilyName(FamilySymbol symbol)
	{
		return string.IsNullOrWhiteSpace(symbol.FamilyName) ? "(Không rõ Family)" : symbol.FamilyName.Trim();
	}

	private static string GetTitleBlockTypeLabel(Document document, ElementId typeId)
	{
		if (!(document.GetElement(typeId) is FamilySymbol symbol))
		{
			return "(Không xác định)";
		}
		string family = GetFamilyName(symbol);
		string type = (string.IsNullOrWhiteSpace(symbol.Name) ? "(Không rõ Type)" : symbol.Name.Trim());
		return family + " - " + type;
	}
}
