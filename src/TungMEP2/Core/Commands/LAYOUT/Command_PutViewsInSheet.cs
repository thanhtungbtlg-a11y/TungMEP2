using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_PutViewsInSheet : SecuredExternalCommand
{
	private const string DialogTitle = "Đưa View vào Sheet";

	protected override void ExecuteCore()
	{
		try
		{
			bool hasOpenSheet = false;
			bool hasOpenNonSheet = false;
			foreach (UIView uiView in base.UiDocument.GetOpenUIViews())
			{
				Element openViewElement = base.Document.GetElement(uiView.ViewId);
				if (!(openViewElement is View view))
				{
					continue;
				}
				if (view is ViewSheet openSheet)
				{
					if (!openSheet.IsPlaceholder)
					{
						hasOpenSheet = true;
					}
				}
				else
				{
					if (view.IsTemplate || view is ViewSchedule { IsTitleblockRevisionSchedule: not false })
					{
						continue;
					}
					hasOpenNonSheet = true;
				}
				if (!(hasOpenSheet && hasOpenNonSheet))
				{
					continue;
				}
				break;
			}
			if (!hasOpenSheet || !hasOpenNonSheet)
			{
				string message = ((!hasOpenSheet && !hasOpenNonSheet) ? "Không tìm thấy Sheet và View/Schedule nào đang được mở.\n\nVui lòng mở ít nhất 1 Sheet (không phải Sheet placeholder) và ít nhất 1 View hoặc Schedule (không phải Template, không phải Titleblock Revision Schedule), sau đó chạy lại lệnh." : (hasOpenSheet ? "Không tìm thấy View/Schedule nào (không phải Sheet) đang được mở.\n\nVui lòng mở ít nhất 1 View hoặc Schedule (không phải Template, không phải Titleblock Revision Schedule) và ít nhất 1 Sheet (không phải Sheet placeholder), sau đó chạy lại lệnh." : "Không tìm thấy Sheet nào đang được mở.\n\nVui lòng mở ít nhất 1 Sheet (không phải Sheet placeholder) và ít nhất 1 View hoặc Schedule (không phải Template, không phải Titleblock Revision Schedule), sau đó chạy lại lệnh."));
				TaskDialog.Show("Đưa View vào Sheet", message);
				return;
			}
			UI_PutViewsInSheetWindow window = new UI_PutViewsInSheetWindow(base.UiDocument);
			if (window.ShowDialog() != true || window.SelectedViewIds.Count == 0 || window.SelectedSheetId == null)
			{
				return;
			}
			ElementId sheetId = window.SelectedSheetId;
			Element sheetElement = base.Document.GetElement(sheetId);
			if (!(sheetElement is ViewSheet sheet))
			{
				TaskDialog.Show("Đưa View vào Sheet", "Đối tượng đích đã chọn không phải Sheet. Vui lòng chọn lại.");
				return;
			}
			string sheetDisplay = sheet.SheetNumber + " - " + sheet.Name;
			XYZ center = GetSheetCenter(sheet);
			HashSet<ElementId> scheduleIdsOnSheet = new HashSet<ElementId>();
			foreach (Element e in new FilteredElementCollector(base.Document, sheet.Id).OfClass(typeof(ScheduleSheetInstance)))
			{
				if (e is ScheduleSheetInstance instance && instance.ScheduleId != ElementId.InvalidElementId)
				{
					scheduleIdsOnSheet.Add(instance.ScheduleId);
				}
			}
			List<string> report = new List<string>();
			int success = 0;
			int skipped = 0;
			int failed = 0;
			using TransactionGroup group = new TransactionGroup(base.Document, "Place Views On Sheet");
			group.Start();
			foreach (ElementId viewId in window.SelectedViewIds)
			{
				Element element = base.Document.GetElement(viewId);
				if (!(element is View selectedView))
				{
					skipped++;
					report.Add($"SKIP: [{viewId.Value}] - Không phải View hoặc Schedule.");
					continue;
				}
				if (selectedView is ViewSheet)
				{
					skipped++;
					report.Add("SKIP: " + selectedView.Name + " - Không thể đặt một Sheet lên một Sheet khác.");
					continue;
				}
				ViewSchedule schedule = selectedView as ViewSchedule;
				if (schedule != null)
				{
					if (scheduleIdsOnSheet.Contains(schedule.Id))
					{
						skipped++;
						report.Add("SKIP: " + schedule.Name + " - Schedule đã tồn tại trên sheet '" + sheetDisplay + "'.");
						continue;
					}
				}
				else if (!Viewport.CanAddViewToSheet(base.Document, sheet.Id, selectedView.Id))
				{
					skipped++;
					report.Add("SKIP: " + selectedView.Name + " - Không thể đặt lên sheet '" + sheetDisplay + "'.");
					continue;
				}
				using Transaction transaction = new Transaction(base.Document, "Place View On Sheet");
				transaction.Start();
				try
				{
					if (schedule != null)
					{
						ScheduleSheetInstance.Create(base.Document, sheet.Id, schedule.Id, center);
						scheduleIdsOnSheet.Add(schedule.Id);
					}
					else
					{
						Viewport.Create(base.Document, sheet.Id, selectedView.Id, center);
					}
					transaction.Commit();
					success++;
					report.Add("OK: " + selectedView.Name);
				}
				catch (Autodesk.Revit.Exceptions.OperationCanceledException)
				{
					if (transaction.GetStatus() == TransactionStatus.Started)
					{
						transaction.RollBack();
					}
					group.RollBack();
					return;
				}
				catch (Exception ex2)
				{
					if (transaction.GetStatus() == TransactionStatus.Started)
					{
						transaction.RollBack();
					}
					failed++;
					report.Add("FAIL: " + selectedView.Name + " - " + ex2.Message);
				}
			}
			group.Assimilate();
			TaskDialog.Show("Đưa View vào Sheet", "Sheet: " + sheetDisplay + "\n" + $"Success: {success}\n" + $"Skipped: {skipped}\n" + $"Failed: {failed}\n\n" + string.Join("\n", report));
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex4)
		{
			TaskDialog.Show("Đưa View vào Sheet", "Đã xảy ra lỗi khi đưa view/schedule vào sheet.\n\nLý do: " + ex4.Message);
		}
	}

	private static XYZ GetSheetCenter(ViewSheet sheet)
	{
		BoundingBoxUV outline = sheet.Outline;
		UV min = outline.Min;
		UV max = outline.Max;
		return new XYZ((min.U + max.U) * 0.5, (min.V + max.V) * 0.5, 0.0);
	}
}
