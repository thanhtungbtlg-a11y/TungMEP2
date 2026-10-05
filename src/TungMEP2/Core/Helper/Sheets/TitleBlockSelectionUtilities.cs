using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Helper.Sheets;

public static class TitleBlockSelectionUtilities
{
	public static List<ViewSheet> GetSheetsFromSelectedSchedules(UIDocument uiDocument, Document document)
	{
		List<long> selectedSheetIds = new List<long>();
		HashSet<long> selectedScheduleIds = new HashSet<long>();
		foreach (ElementId id in uiDocument.Selection.GetElementIds())
		{
			Element element = document.GetElement(id);
			if (element is ViewSheet { IsPlaceholder: false } sheet)
			{
				selectedSheetIds.Add(sheet.Id.Value);
			}
			else if (element is ScheduleSheetInstance scheduleSheetInstance && !(scheduleSheetInstance.ScheduleId == ElementId.InvalidElementId))
			{
				selectedScheduleIds.Add(scheduleSheetInstance.ScheduleId.Value);
			}
		}
		if (selectedSheetIds.Count > 0)
		{
			return (from idValue in selectedSheetIds.Distinct()
				select document.GetElement(new ElementId(idValue)) as ViewSheet into result
				where result != null && !result.IsPlaceholder
				select (result) into viewSheet
				orderby viewSheet.SheetNumber
				select viewSheet).ToList();
		}
		if (selectedScheduleIds.Count == 0)
		{
			throw new InvalidOperationException("Hãy chọn sheet trên Project Browser hoặc chọn Schedule trên Sheet trước khi chạy lệnh.");
		}
		List<long> sheetIds = new List<long>();
		foreach (long scheduleIdValue in selectedScheduleIds)
		{
			ElementId scheduleId = new ElementId(scheduleIdValue);
			if (!(document.GetElement(scheduleId) is ViewSchedule viewSchedule))
			{
				throw new InvalidOperationException("Schedule được chọn không hợp lệ.");
			}
			ElementId categoryId = viewSchedule.Definition?.CategoryId;
			if (categoryId == null || categoryId != new ElementId(BuiltInCategory.OST_Sheets))
			{
				throw new InvalidOperationException("Schedule '" + viewSchedule.Name + "' không phải Sheet List Schedule (OST_Sheets).");
			}
			List<long> sheetIdsInSchedule = (from ViewSheet viewSheet in new FilteredElementCollector(document, viewSchedule.Id).OfClass(typeof(ViewSheet))
				where !viewSheet.IsPlaceholder
				select viewSheet.Id.Value).Distinct().ToList();
			if (sheetIdsInSchedule.Count == 0)
			{
				throw new InvalidOperationException("Schedule '" + viewSchedule.Name + "' không có sheet hợp lệ.");
			}
			sheetIds.AddRange(sheetIdsInSchedule);
		}
		return (from idValue in sheetIds.Distinct()
			select document.GetElement(new ElementId(idValue)) as ViewSheet into result
			where result != null && !result.IsPlaceholder
			select (result) into viewSheet
			orderby viewSheet.SheetNumber
			select viewSheet).ToList();
	}

	public static ViewSheetSet PickPrintSet(Document document)
	{
		List<ViewSheetSet> printSets = (from ViewSheetSet set in new FilteredElementCollector(document).OfClass(typeof(ViewSheetSet))
			orderby set.Name
			select set).ToList();
		if (printSets.Count == 0)
		{
			throw new InvalidOperationException("Không có Print Set trong model.");
		}
		Window window = new Window
		{
			Title = "Chọn Print Set",
			Width = 420.0,
			Height = 170.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = "Chọn 1 Print Set:"
		});
		System.Windows.Controls.ComboBox comboBox = new System.Windows.Controls.ComboBox
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0),
			ItemsSource = printSets,
			DisplayMemberPath = "Name",
			SelectedIndex = 0
		};
		root.Children.Add(comboBox);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button okButton = new Button
		{
			Content = "Đồng ý",
			Width = 80.0,
			IsDefault = true,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		Button cancelButton = new Button
		{
			Content = "Hủy",
			Width = 80.0,
			IsCancel = true
		};
		okButton.Click += delegate
		{
			window.DialogResult = true;
		};
		cancelButton.Click += delegate
		{
			window.DialogResult = false;
		};
		buttons.Children.Add(okButton);
		buttons.Children.Add(cancelButton);
		root.Children.Add(buttons);
		window.Content = root;
		if (window.ShowDialog() != true)
		{
			throw new OperationCanceledException();
		}
		if (comboBox.SelectedItem is ViewSheetSet selected)
		{
			return selected;
		}
		throw new InvalidOperationException("Không thể xác định Print Set được chọn.");
	}

	public static List<ViewSheet> GetSheetsFromPrintSet(ViewSheetSet printSet)
	{
		List<ViewSheet> sheets = new List<ViewSheet>();
		ViewSet views = printSet.Views;
		foreach (View view in views)
		{
			if (view is ViewSheet { IsPlaceholder: false } sheet)
			{
				sheets.Add(sheet);
			}
		}
		return (from viewSheet in sheets
			group viewSheet by viewSheet.Id into @group
			select @group.First() into viewSheet
			orderby viewSheet.SheetNumber
			select viewSheet).ToList();
	}

	public static List<ElementId> CollectTitleBlockIds(Document document, IEnumerable<ViewSheet> sheets)
	{
		List<ElementId> allIds = new List<ElementId>();
		foreach (ViewSheet sheet in sheets)
		{
			List<ElementId> titleBlockIds = (from instance in new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>()
				select instance.Id).ToList();
			allIds.AddRange(titleBlockIds);
		}
		return allIds.Distinct().ToList();
	}

	public static List<string> BuildTitleBlockTypeCountLines(Document document, IEnumerable<ElementId> titleBlockIds)
	{
		List<string> lines = new List<string>();
		List<(string, int)> groupedByType = (from item in (from instance in titleBlockIds.Select((ElementId id) => document.GetElement(id)).OfType<FamilyInstance>()
				group instance by instance.GetTypeId()).Select(delegate(IGrouping<ElementId, FamilyInstance> @group)
			{
				ElementId key = @group.Key;
				int item = @group.Count();
				string titleBlockTypeLabel = GetTitleBlockTypeLabel(document, key);
				return (label: titleBlockTypeLabel, count: item);
			})
			orderby item.count descending, item.label
			select item).ToList();
		foreach (var (label, count) in groupedByType)
		{
			lines.Add($"{label}: {count}");
		}
		return lines;
	}

	public static List<string> BuildSheetTitleBlockUsageLines(Document document, IEnumerable<ViewSheet> sheets)
	{
		List<string> lines = new List<string>();
		foreach (ViewSheet sheet in sheets.OrderBy((ViewSheet x) => x.SheetNumber))
		{
			List<FamilyInstance> titleBlocks = new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>()
				.ToList();
			if (titleBlocks.Count == 0)
			{
				lines.Add(sheet.SheetNumber + " - " + sheet.Name + ": (không có titleblock)");
				continue;
			}
			string usageText = string.Join(", ", from x in (from instance in titleBlocks
					group instance by instance.GetTypeId()).Select(delegate(IGrouping<ElementId, FamilyInstance> @group)
				{
					string titleBlockTypeLabel = GetTitleBlockTypeLabel(document, @group.Key);
					return $"{titleBlockTypeLabel} ({@group.Count()})";
				})
				orderby x
				select x);
			lines.Add(sheet.SheetNumber + " - " + sheet.Name + ": " + usageText);
		}
		return lines;
	}

	private static string GetTitleBlockTypeLabel(Document document, ElementId typeId)
	{
		if (!(document.GetElement(typeId) is FamilySymbol symbol))
		{
			return $"(Không xác định) [{typeId.Value}]";
		}
		string familyName = (string.IsNullOrWhiteSpace(symbol.FamilyName) ? "(Không rõ Family)" : symbol.FamilyName);
		string typeName = (string.IsNullOrWhiteSpace(symbol.Name) ? "(Không rõ Type)" : symbol.Name);
		return familyName + " - " + typeName;
	}

	public static void ShowTitleBlockTypeCountWindow(string header, IList<string> typeCountLines, IList<string> sheetUsageLines)
	{
		Window window = new Window
		{
			Title = "CheckListTitleBlocks",
			Width = 560.0,
			Height = 620.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.CanResize,
			MinWidth = 420.0,
			MinHeight = 360.0
		};
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = header,
			TextWrapping = TextWrapping.Wrap
		});
		root.Children.Add(new TextBlock
		{
			Text = "Thống kê theo loại TitleBlock",
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		});
		ListBox typeCountListBox = new ListBox
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			Height = 180.0,
			ItemsSource = typeCountLines
		};
		root.Children.Add(typeCountListBox);
		root.Children.Add(new TextBlock
		{
			Text = "Chi tiết sheet sử dụng TitleBlock",
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		});
		ListBox sheetUsageListBox = new ListBox
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			Height = 280.0,
			ItemsSource = sheetUsageLines
		};
		root.Children.Add(sheetUsageListBox);
		window.Content = root;
		window.ShowDialog();
	}
}
