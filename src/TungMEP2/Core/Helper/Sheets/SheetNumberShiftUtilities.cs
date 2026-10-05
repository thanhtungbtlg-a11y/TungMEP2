using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Helper.Sheets;

public static class SheetNumberShiftUtilities
{
	public static SheetNumberShiftReport ShiftSelectedSheets(UIDocument uiDocument, int delta)
	{
		if (delta == 0)
		{
			throw new ArgumentException("Delta không hợp lệ.", "delta");
		}
		Document document = uiDocument.Document;
		List<ViewSheet> sheets = ResolveSheetsFromSelection(uiDocument, document);
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Hãy chọn sheet trên Project Browser hoặc chọn Schedule trên Sheet trước khi chạy lệnh.");
		}
		List<ViewSheet> orderedSheets = sheets.OrderBy((ViewSheet viewSheet) => viewSheet.SheetNumber, StringComparer.Ordinal).ToList();
		if (delta > 0)
		{
			orderedSheets.Reverse();
		}
		SheetNumberShiftReport report = new SheetNumberShiftReport
		{
			Delta = delta,
			TotalCount = orderedSheets.Count
		};
		using TransactionGroup transactionGroup = new TransactionGroup(document, "Shift Sheets");
		transactionGroup.Start();
		foreach (ViewSheet sheet in orderedSheets)
		{
			using Transaction transaction = new Transaction(document, "Shift Single Sheet");
			transaction.Start();
			string currentSheetNumber = sheet.SheetNumber;
			try
			{
				if (!TryShiftSheetNumber(currentSheetNumber, delta, out string newSheetNumber, out string reason))
				{
					transaction.RollBack();
					report.SkippedCount++;
					report.Messages.Add("SKIP: " + sheet.SheetNumber + " - " + reason);
				}
				else if (string.Equals(currentSheetNumber, newSheetNumber, StringComparison.Ordinal))
				{
					transaction.RollBack();
					report.SkippedCount++;
					report.Messages.Add("SKIP: " + sheet.SheetNumber + " - Sheet number không thay đổi.");
				}
				else
				{
					sheet.SheetNumber = newSheetNumber;
					document.Regenerate();
					transaction.Commit();
					report.SuccessCount++;
					report.Messages.Add("OK: " + currentSheetNumber + " -> " + newSheetNumber);
				}
			}
			catch (Exception ex)
			{
				if (transaction.HasStarted())
				{
					transaction.RollBack();
				}
				report.FailedCount++;
				report.Messages.Add("FAIL: " + currentSheetNumber + " - " + ex.Message);
			}
		}
		transactionGroup.Assimilate();
		return report;
	}

	private static List<ViewSheet> ResolveSheetsFromSelection(UIDocument uiDocument, Document document)
	{
		IList<ElementId> selectionIds = uiDocument.Selection.GetElementIds().ToList();
		if (selectionIds.Count == 0)
		{
			return new List<ViewSheet>();
		}
		List<long> selectedSheetIds = new List<long>();
		HashSet<long> selectedScheduleIds = new HashSet<long>();
		foreach (ElementId id in selectionIds)
		{
			Element element = document.GetElement(id);
			if (element is ViewSheet sheet)
			{
				if (!sheet.IsPlaceholder)
				{
					selectedSheetIds.Add(sheet.Id.Value);
				}
			}
			else if (element is ScheduleSheetInstance scheduleSheetInstance && scheduleSheetInstance.ScheduleId != ElementId.InvalidElementId)
			{
				selectedScheduleIds.Add(scheduleSheetInstance.ScheduleId.Value);
			}
		}
		if (selectedSheetIds.Count > 0)
		{
			return (from idValue in selectedSheetIds.Distinct()
				select document.GetElement(new ElementId(idValue)) as ViewSheet into result
				where result != null && !result.IsPlaceholder
				select (result)).ToList();
		}
		if (selectedScheduleIds.Count == 0)
		{
			return new List<ViewSheet>();
		}
		List<long> sheetIdsFromSchedules = new List<long>();
		foreach (long scheduleIdValue in selectedScheduleIds)
		{
			ElementId scheduleId = new ElementId(scheduleIdValue);
			if (!(document.GetElement(scheduleId) is ViewSchedule viewSchedule))
			{
				continue;
			}
			ElementId categoryId = viewSchedule.Definition?.CategoryId;
			if (!(categoryId == null) && !(categoryId != new ElementId(BuiltInCategory.OST_Sheets)))
			{
				List<long> scheduleSheetIds = (from ViewSheet viewSheet in new FilteredElementCollector(document, viewSchedule.Id).OfClass(typeof(ViewSheet))
					where !viewSheet.IsPlaceholder
					select viewSheet.Id.Value).Distinct().ToList();
				sheetIdsFromSchedules.AddRange(scheduleSheetIds);
			}
		}
		return (from idValue in sheetIdsFromSchedules.Distinct()
			select document.GetElement(new ElementId(idValue)) as ViewSheet into result
			where result != null && !result.IsPlaceholder
			select (result)).ToList();
	}

	private static bool TryShiftSheetNumber(string currentSheetNumber, int delta, out string newSheetNumber, out string reason)
	{
		newSheetNumber = currentSheetNumber;
		reason = string.Empty;
		if (string.IsNullOrWhiteSpace(currentSheetNumber))
		{
			reason = "Sheet number rỗng.";
			return false;
		}
		int end = currentSheetNumber.Length - 1;
		while (end >= 0 && !char.IsDigit(currentSheetNumber[end]))
		{
			end--;
		}
		if (end < 0)
		{
			reason = "Không tìm thấy cụm số để dịch chuyển.";
			return false;
		}
		int start = end;
		while (start >= 0 && char.IsDigit(currentSheetNumber[start]))
		{
			start--;
		}
		start++;
		string numericPart = currentSheetNumber.Substring(start, end - start + 1);
		if (!int.TryParse(numericPart, out var numberValue))
		{
			reason = "Không thể đọc giá trị số hiện tại.";
			return false;
		}
		int shiftedValue = numberValue + delta;
		if (shiftedValue < 0)
		{
			reason = "Giá trị sau khi giảm nhỏ hơn 0.";
			return false;
		}
		int width = numericPart.Length;
		string shiftedPart = shiftedValue.ToString().PadLeft(width, '0');
		string prefix = currentSheetNumber.Substring(0, start);
		string suffix = currentSheetNumber.Substring(end + 1);
		newSheetNumber = prefix + shiftedPart + suffix;
		return true;
	}
}
