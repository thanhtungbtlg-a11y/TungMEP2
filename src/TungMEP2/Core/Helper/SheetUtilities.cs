using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Helper;

public static class SheetUtilities
{
	public static void SetSttBySheetIds(Document document, IList<int> sheetIds, int start)
	{
		if (sheetIds == null || sheetIds.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy Sheet hợp lệ trong danh sách đầu vào.");
		}
		List<ViewSheet> sheets = new List<ViewSheet>();
		foreach (int sheetId in sheetIds)
		{
			if (document.GetElement(new ElementId(sheetId)) is ViewSheet { IsPlaceholder: false } sheet)
			{
				sheets.Add(sheet);
			}
		}
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy Sheet hợp lệ trong danh sách đầu vào.");
		}
		sheets.Sort((ViewSheet left, ViewSheet right) => CompareSheetNumberNatural(left.SheetNumber, right.SheetNumber));
		using Transaction tx = new Transaction(document, "Set STT");
		tx.Start();
		for (int i = 0; i < sheets.Count; i++)
		{
			ViewSheet sheet2 = sheets[i];
			Parameter stt = sheet2.LookupParameter("STT") ?? throw new InvalidOperationException("Sheet '" + sheet2.SheetNumber + " - " + sheet2.Name + "' không có Parameter 'STT'.");
			if (stt.StorageType != StorageType.String)
			{
				throw new InvalidOperationException("Parameter 'STT' phải là Text (String).");
			}
			stt.Set((start + i).ToString());
		}
		tx.Commit();
	}

	public static int SetSttValues(
		Document document,
		IEnumerable<KeyValuePair<ViewSheet, string>> assignments)
	{
		if (document == null)
		{
			throw new ArgumentNullException(nameof(document));
		}
		if (assignments == null)
		{
			throw new ArgumentNullException(nameof(assignments));
		}

		List<KeyValuePair<ViewSheet, string>> values = assignments
			.Where(item =>
				item.Key != null &&
				!item.Key.IsPlaceholder &&
				item.Key.Document.Equals(document))
			.GroupBy(item => item.Key.Id.Value)
			.Select(group => group.First())
			.Select(item => new KeyValuePair<ViewSheet, string>(
				item.Key,
				item.Value?.Trim() ?? string.Empty))
			.ToList();
		if (values.Count == 0)
		{
			throw new InvalidOperationException(
				"Không có Sheet hợp lệ để ghi STT.");
		}

		List<Parameter> parameters = new List<Parameter>();
		foreach (KeyValuePair<ViewSheet, string> item in values)
		{
			if (string.IsNullOrWhiteSpace(item.Value))
			{
				throw new InvalidOperationException(
					$"STT của sheet '{item.Key.SheetNumber}' đang để trống.");
			}

			Parameter parameter = item.Key.LookupParameter("STT");
			if (parameter == null)
			{
				throw new InvalidOperationException(
					$"Sheet '{item.Key.SheetNumber} - {item.Key.Name}' không có Parameter 'STT'.");
			}
			if (parameter.StorageType != StorageType.String)
			{
				throw new InvalidOperationException(
					$"Parameter 'STT' của sheet '{item.Key.SheetNumber}' phải là Text (String).");
			}
			if (parameter.IsReadOnly)
			{
				throw new InvalidOperationException(
					$"Parameter 'STT' của sheet '{item.Key.SheetNumber}' đang ở trạng thái chỉ đọc.");
			}
			parameters.Add(parameter);
		}

		using Transaction transaction =
			new Transaction(document, "PrintSheets - Set STT theo danh sách");
		try
		{
			transaction.Start();
			for (int i = 0; i < values.Count; i++)
			{
				parameters[i].Set(values[i].Value);
			}
			transaction.Commit();
		}
		catch
		{
			if (transaction.GetStatus() == TransactionStatus.Started)
			{
				transaction.RollBack();
			}
			throw;
		}

		return values.Count;
	}

	public static int EnsureSttForExport(
		Document document,
		IEnumerable<ViewSheet> selectedSheets,
		int start = 1)
	{
		if (document == null)
		{
			throw new ArgumentNullException(nameof(document));
		}
		if (selectedSheets == null)
		{
			throw new ArgumentNullException(nameof(selectedSheets));
		}
		if (start < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(start),
				"STT tự động phải bắt đầu từ 1 trở lên.");
		}

		List<ViewSheet> sheets = selectedSheets
			.Where(sheet =>
				sheet != null &&
				!sheet.IsPlaceholder &&
				sheet.Document.Equals(document))
			.GroupBy(sheet => sheet.Id.Value)
			.Select(group => group.First())
			.ToList();
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException(
				"Không tìm thấy Sheet hợp lệ để kiểm tra STT trước khi xuất.");
		}

		// STT follows the exact order shown in PrintSheets.
		List<Parameter> sttParameters = sheets
			.Select(sheet => sheet.LookupParameter("STT"))
			.ToList();
		bool hasUnsupportedParameter = sttParameters.Any(parameter =>
			parameter == null ||
			parameter.StorageType != StorageType.String);
		bool requiresRepair = hasUnsupportedParameter ||
			RequiresAutomaticSttRepair(
				sttParameters.Select(parameter =>
					parameter?.StorageType == StorageType.String
						? parameter.AsString()
						: null),
				start);
		if (!requiresRepair)
		{
			return 0;
		}

		for (int i = 0; i < sheets.Count; i++)
		{
			ViewSheet sheet = sheets[i];
			Parameter stt = sttParameters[i];
			if (stt == null)
			{
				throw new InvalidOperationException(
					"Sheet '" + sheet.SheetNumber + " - " + sheet.Name +
					"' không có Parameter 'STT'.");
			}
			if (stt.StorageType != StorageType.String)
			{
				throw new InvalidOperationException(
					"Parameter 'STT' của sheet '" + sheet.SheetNumber +
					"' phải là Text (String).");
			}
			if (stt.IsReadOnly)
			{
				throw new InvalidOperationException(
					"Parameter 'STT' của sheet '" + sheet.SheetNumber +
					"' đang ở trạng thái chỉ đọc nên PrintSheets không thể tự sửa.");
			}
		}

		using Transaction tx =
			new Transaction(document, "PrintSheets - Auto Set STT");
		try
		{
			tx.Start();
			for (int i = 0; i < sheets.Count; i++)
			{
				sheets[i]
					.LookupParameter("STT")
					.Set((start + i).ToString(CultureInfo.InvariantCulture));
			}
			tx.Commit();
		}
		catch
		{
			if (tx.GetStatus() == TransactionStatus.Started)
			{
				tx.RollBack();
			}
			throw;
		}

		return sheets.Count;
	}

	internal static bool RequiresAutomaticSttRepair(
		IEnumerable<string> rawValues,
		int start)
	{
		if (rawValues == null || start < 1)
		{
			return true;
		}

		int expectedValue = start;
		foreach (string rawValue in rawValues)
		{
			if (string.IsNullOrWhiteSpace(rawValue))
			{
				return true;
			}

			string trimmedValue = rawValue.Trim();
			if ((!int.TryParse(
					trimmedValue,
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out int sttValue) &&
				!int.TryParse(
					trimmedValue,
					NumberStyles.Integer,
					CultureInfo.CurrentCulture,
					out sttValue)) ||
				sttValue != expectedValue)
			{
				return true;
			}

			expectedValue++;
		}

		return false;
	}

	public static void CreateOrReplaceSelectPrintSetBySheetIds(Document document, IList<int> sheetIds, string printSetName)
	{
		if (string.IsNullOrWhiteSpace(printSetName))
		{
			throw new InvalidOperationException("Tên Select Print Set không được để trống.");
		}
		if (sheetIds == null || sheetIds.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong danh sách chọn.");
		}
		List<ViewSheet> sheets = new List<ViewSheet>();
		foreach (int sheetId in sheetIds)
		{
			if (document.GetElement(new ElementId(sheetId)) is ViewSheet { IsPlaceholder: false } sheet)
			{
				sheets.Add(sheet);
			}
		}
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong danh sách chọn.");
		}
		sheets.Sort((ViewSheet left, ViewSheet right) => CompareSheetNumberNatural(left.SheetNumber, right.SheetNumber));
		CreateOrReplaceSelectPrintSet(document, sheets, printSetName.Trim());
	}

	public static void SetStt(UIDocument uiDocument)
	{
		Document document = uiDocument.Document;
		int? start = PromptForStartStt();
		if (!start.HasValue)
		{
			throw new OperationCanceledException();
		}
		IList<ElementId> selectionIds = uiDocument.Selection.GetElementIds().ToList();
		if (selectionIds.Count == 0)
		{
			throw new InvalidOperationException("Hãy chọn Sheet trên Project Browser hoặc chọn Schedule trên Sheet trước khi chạy lệnh.");
		}
		List<long> selectedSheetIds = new List<long>();
		HashSet<long> selectedScheduleIds = new HashSet<long>();
		foreach (ElementId id in selectionIds)
		{
			Element element = document.GetElement(id);
			if (element is ViewSheet sheet)
			{
				selectedSheetIds.Add(sheet.Id.Value);
			}
			else if (element is ScheduleSheetInstance scheduleSheetInstance && !(scheduleSheetInstance.ScheduleId == ElementId.InvalidElementId))
			{
				selectedScheduleIds.Add(scheduleSheetInstance.ScheduleId.Value);
			}
		}
		List<long> sheetIdsFromSchedules = new List<long>();
		if (selectedSheetIds.Count == 0 && selectedScheduleIds.Count > 0)
		{
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
					throw new InvalidOperationException("Schedule '" + viewSchedule.Name + "' không có sheet để set STT.");
				}
				sheetIdsFromSchedules.AddRange(sheetIdsInSchedule);
			}
		}
		IList<long> resolvedSheetIds = SheetSelectionResolver.ResolveSheetIds(selectedSheetIds, sheetIdsFromSchedules);
		if (resolvedSheetIds.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy Sheet hợp lệ trong selection.");
		}
		List<ViewSheet> sheets = new List<ViewSheet>();
		foreach (long sheetId in resolvedSheetIds)
		{
			if (document.GetElement(new ElementId(sheetId)) is ViewSheet { IsPlaceholder: false } sheet2)
			{
				sheets.Add(sheet2);
			}
		}
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy Sheet hợp lệ trong selection.");
		}
		sheets.Sort((ViewSheet left, ViewSheet right) => CompareSheetNumberNatural(left.SheetNumber, right.SheetNumber));
		using Transaction tx = new Transaction(document, "Set STT");
		tx.Start();
		for (int i = 0; i < sheets.Count; i++)
		{
			ViewSheet sheet3 = sheets[i];
			Parameter stt = sheet3.LookupParameter("STT") ?? throw new InvalidOperationException("Sheet '" + sheet3.SheetNumber + " - " + sheet3.Name + "' không có Parameter 'STT'.");
			if (stt.StorageType != StorageType.String)
			{
				throw new InvalidOperationException("Parameter 'STT' phải là Text (String).");
			}
			stt.Set((start.Value + i).ToString());
		}
		tx.Commit();
	}

	public static void CreateSelectPrintSetFromSheets(IList<ViewSheet> sheets)
	{
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong danh sách chọn.");
		}
		List<ViewSheet> validSheets = new List<ViewSheet>();
		foreach (ViewSheet sheet in sheets)
		{
			if (!sheet.IsPlaceholder)
			{
				validSheets.Add(sheet);
			}
		}
		if (validSheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet hợp lệ trong danh sách chọn.");
		}
		Document document = validSheets[0].Document;
		string printSetName = PromptForPrintSetName();
		if (printSetName == null)
		{
			throw new OperationCanceledException();
		}
		if (string.IsNullOrWhiteSpace(printSetName))
		{
			throw new InvalidOperationException("Tên Select Print Set không hợp lệ.");
		}
		CreateOrReplaceSelectPrintSet(document, validSheets, printSetName);
	}

	public static void CreateSelectPrintSetFromProjectBrowserSelection(UIDocument uiDocument)
	{
		CreateSelectPrintSetFromSelection(uiDocument);
	}

	public static void CreateSelectPrintSetFromSelection(UIDocument uiDocument)
	{
		Document document = uiDocument.Document;
		IList<ElementId> selectionIds = uiDocument.Selection.GetElementIds().ToList();
		if (selectionIds.Count == 0)
		{
			throw new InvalidOperationException("Hãy chọn sheet trên Project Browser trước khi chạy lệnh.");
		}
		List<long> selectedSheetIds = new List<long>();
		List<long> selectedScheduleIds = new List<long>();
		HashSet<long> seenScheduleIds = new HashSet<long>();
		foreach (ElementId id in selectionIds)
		{
			Element element = document.GetElement(id);
			if (element is ViewSheet sheet)
			{
				selectedSheetIds.Add(sheet.Id.Value);
			}
			else if (element is ScheduleSheetInstance scheduleSheetInstance && !(scheduleSheetInstance.ScheduleId == ElementId.InvalidElementId))
			{
				long scheduleIdValue = scheduleSheetInstance.ScheduleId.Value;
				if (seenScheduleIds.Add(scheduleIdValue))
				{
					selectedScheduleIds.Add(scheduleIdValue);
				}
			}
		}
		List<long> sheetIdsFromSchedules = new List<long>();
		if (selectedSheetIds.Count == 0 && selectedScheduleIds.Count > 0)
		{
			foreach (long scheduleIdValue2 in selectedScheduleIds)
			{
				ElementId scheduleId = new ElementId(scheduleIdValue2);
				if (!(document.GetElement(scheduleId) is ViewSchedule viewSchedule))
				{
					throw new InvalidOperationException("Schedule được chọn không hợp lệ.");
				}
				ElementId categoryId = viewSchedule.Definition?.CategoryId;
				if (categoryId == null || categoryId != new ElementId(BuiltInCategory.OST_Sheets))
				{
					throw new InvalidOperationException("Schedule '" + viewSchedule.Name + "' không phải Sheet List Schedule (OST_Sheets).");
				}
				List<long> sheetIdsInSchedule = GetSheetIdsFromScheduleInDisplayedOrder(document, viewSchedule);
				if (sheetIdsInSchedule.Count == 0)
				{
					throw new InvalidOperationException("Schedule '" + viewSchedule.Name + "' không có sheet để in.");
				}
				sheetIdsFromSchedules.AddRange(sheetIdsInSchedule);
			}
		}
		IList<long> resolvedSheetIds = SheetSelectionResolver.ResolveSheetIds(selectedSheetIds, sheetIdsFromSchedules);
		if (resolvedSheetIds.Count == 0)
		{
			throw new InvalidOperationException("Hãy chọn sheet trên Project Browser hoặc chọn schedule trên sheet trước khi chạy lệnh.");
		}
		List<ViewSheet> sheets = new List<ViewSheet>();
		foreach (long sheetId in resolvedSheetIds)
		{
			if (document.GetElement(new ElementId(sheetId)) is ViewSheet sheet2)
			{
				sheets.Add(sheet2);
			}
		}
		CreateSelectPrintSetFromSheets(sheets);
	}

	public static void PlaceOpenViewOnSingleOpenSheet(UIDocument uiDocument)
	{
		Document document = uiDocument.Document;
		List<ViewSheet> openSheets = new List<ViewSheet>();
		List<View> openNonSheetViews = new List<View>();
		foreach (UIView uiView in uiDocument.GetOpenUIViews())
		{
			if (document.GetElement(uiView.ViewId) is View { IsTemplate: false } view)
			{
				if (view is ViewSheet sheet)
				{
					openSheets.Add(sheet);
				}
				else
				{
					openNonSheetViews.Add(view);
				}
			}
		}
		if (openSheets.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy sheet đang mở. Hãy mở 1 sheet trước khi chạy lệnh.");
		}
		if (openSheets.Count > 1)
		{
			throw new InvalidOperationException("Đang mở nhiều sheet. Hãy đóng bớt, chỉ để lại 1 sheet.");
		}
		if (openNonSheetViews.Count == 0)
		{
			throw new InvalidOperationException("Không tìm thấy view (không phải sheet) đang mở. Hãy mở 1 view trước khi chạy lệnh.");
		}
		if (openNonSheetViews.Count > 1)
		{
			throw new InvalidOperationException("Đang mở nhiều view (không phải sheet). Hãy đóng bớt, chỉ để lại 1 view.");
		}
		ViewSheet sheetToPlaceOn = openSheets[0];
		View viewToPlace = openNonSheetViews[0];
		BoundingBoxUV outline = sheetToPlaceOn.Outline;
		UV min = outline.Min;
		UV max = outline.Max;
		XYZ center = new XYZ((min.U + max.U) * 0.5, (min.V + max.V) * 0.5, 0.0);
		using Transaction tx = new Transaction(document, "Place Open View On Sheet");
		tx.Start();
		if (!Viewport.CanAddViewToSheet(document, sheetToPlaceOn.Id, viewToPlace.Id))
		{
			throw new InvalidOperationException("Không thể đặt view '" + viewToPlace.Name + "' lên sheet '" + sheetToPlaceOn.SheetNumber + " - " + sheetToPlaceOn.Name + "'.");
		}
		Viewport.Create(document, sheetToPlaceOn.Id, viewToPlace.Id, center);
		tx.Commit();
	}

	public static void CreateOrReplaceSelectPrintSet(Document document, IList<ViewSheet> sheetsInOrder, string printSetName)
	{
		PrintManager pm = document.PrintManager;
		pm.PrintRange = PrintRange.Select;
		ViewSheetSetting vss = pm.ViewSheetSetting;
		using Transaction tx = new Transaction(document, "Create Select Print Set");
		tx.Start();
		ViewSheetSet existing = FindViewSheetSetByName(document, printSetName);
		if (existing != null)
		{
			vss.CurrentViewSheetSet = existing;
			if (!vss.Delete())
			{
				throw new InvalidOperationException("Không thể xóa Select Print Set '" + printSetName + "'.");
			}
		}
		InSessionViewSheetSet inSession = (InSessionViewSheetSet)(vss.CurrentViewSheetSet = vss.InSession);
		ViewSet viewSet = new ViewSet();
		foreach (ViewSheet sheet in sheetsInOrder)
		{
			viewSet.Insert(sheet);
		}
		inSession.Views = viewSet;
		IViewSheetSet orderedSet = inSession;
		if (orderedSet != null)
		{
			orderedSet.IsAutomatic = false;
			orderedSet.OrderedViewList = sheetsInOrder.Cast<View>().ToList();
		}
		if (!vss.SaveAs(printSetName))
		{
			throw new InvalidOperationException("Không thể tạo Select Print Set '" + printSetName + "'.");
		}
		ViewSheetSet created = FindViewSheetSetByName(document, printSetName);
		if (created == null)
		{
			document.Regenerate();
			created = FindViewSheetSetByName(document, printSetName);
		}
		if (created == null)
		{
			throw new InvalidOperationException("Không tìm thấy Select Print Set '" + printSetName + "' sau khi tạo.");
		}
		vss.CurrentViewSheetSet = created;
		tx.Commit();
	}

	private static ViewSheetSet? FindViewSheetSetByName(Document document, string name)
	{
		return new FilteredElementCollector(document).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().FirstOrDefault((ViewSheetSet set) => string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	private static string? PromptForPrintSetName()
	{
		Window window = new Window
		{
			Title = "Tên Print Set",
			Width = 360.0,
			Height = 160.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = "Tên Select Print Set:"
		});
		System.Windows.Controls.TextBox nameBox = new System.Windows.Controls.TextBox
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0),
			Text = "VP"
		};
		root.Children.Add(nameBox);
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
		return (window.ShowDialog() == true) ? nameBox.Text.Trim() : null;
	}

	private static int? PromptForStartStt()
	{
		Window window = new Window
		{
			Title = "Set STT",
			Width = 300.0,
			Height = 150.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = "Nhập STT bắt đầu:"
		});
		System.Windows.Controls.TextBox valueBox = new System.Windows.Controls.TextBox
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0),
			Text = "1"
		};
		root.Children.Add(valueBox);
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
			return null;
		}
		string raw = valueBox.Text.Trim();
		if (!int.TryParse(raw, out var value) || value < 0)
		{
			throw new InvalidOperationException("Số bắt đầu không hợp lệ.");
		}
		return value;
	}

	private static int CompareSheetNumberNatural(string left, string right)
	{
		if ((object)left == right)
		{
			return 0;
		}
		int i = 0;
		int j = 0;
		while (i < left.Length && j < right.Length)
		{
			char c1 = left[i];
			char c2 = right[j];
			bool d1 = char.IsDigit(c1);
			bool d2 = char.IsDigit(c2);
			if (d1 && d2)
			{
				int start1 = i;
				int start2 = j;
				long n1 = 0L;
				long n2 = 0L;
				for (; i < left.Length && char.IsDigit(left[i]); i++)
				{
					n1 = n1 * 10 + (left[i] - 48);
				}
				for (; j < right.Length && char.IsDigit(right[j]); j++)
				{
					n2 = n2 * 10 + (right[j] - 48);
				}
				if (n1 != n2)
				{
					return (n1 >= n2) ? 1 : (-1);
				}
				int len1 = i - start1;
				int len2 = j - start2;
				if (len1 != len2)
				{
					return (len1 >= len2) ? 1 : (-1);
				}
			}
			else
			{
				if (d1 != d2)
				{
					return (!d1) ? 1 : (-1);
				}
				int cmp = char.ToUpperInvariant(c1).CompareTo(char.ToUpperInvariant(c2));
				if (cmp != 0)
				{
					return cmp;
				}
				i++;
				j++;
			}
		}
		return left.Length.CompareTo(right.Length);
	}

	private static List<long> GetSheetIdsFromScheduleInDisplayedOrder(Document document, ViewSchedule viewSchedule)
	{
		List<ViewSheet> sheetsInSchedule = (from ViewSheet sheet in new FilteredElementCollector(document, viewSchedule.Id).OfClass(typeof(ViewSheet))
			where !sheet.IsPlaceholder
			select sheet).ToList();
		if (sheetsInSchedule.Count == 0)
		{
			return new List<long>();
		}
		Dictionary<string, Queue<ViewSheet>> sheetsByNumber = sheetsInSchedule.GroupBy((ViewSheet sheet) => sheet.SheetNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, ViewSheet> group) => group.Key, (IGrouping<string, ViewSheet> group) => new Queue<ViewSheet>(group.OrderBy((ViewSheet sheet) => sheet.Id.Value)), StringComparer.OrdinalIgnoreCase);
		List<long> orderedSheetIds = new List<long>();
		TableSectionData bodySection = viewSchedule.GetTableData().GetSectionData(SectionType.Body);
		for (int row = bodySection.FirstRowNumber; row <= bodySection.LastRowNumber; row++)
		{
			for (int col = bodySection.FirstColumnNumber; col <= bodySection.LastColumnNumber; col++)
			{
				string cellText = (viewSchedule.GetCellText(SectionType.Body, row, col) ?? string.Empty).Trim();
				if (!string.IsNullOrWhiteSpace(cellText) && sheetsByNumber.TryGetValue(cellText, out var candidateQueue) && candidateQueue.Count != 0)
				{
					orderedSheetIds.Add(candidateQueue.Dequeue().Id.Value);
					break;
				}
			}
		}
		if (orderedSheetIds.Count == 0)
		{
			return sheetsInSchedule.Select((ViewSheet sheet) => sheet.Id.Value).Distinct().ToList();
		}
		foreach (Queue<ViewSheet> remainingSheets in sheetsByNumber.Values)
		{
			while (remainingSheets.Count > 0)
			{
				orderedSheetIds.Add(remainingSheets.Dequeue().Id.Value);
			}
		}
		return orderedSheetIds.Distinct().ToList();
	}
}
