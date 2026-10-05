using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_CheckListTitleBlocks : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<ViewSheet> sheets;
			if (base.UiDocument.Selection.GetElementIds().Count == 0)
			{
				ViewSheetSet selectedPrintSet = TitleBlockSelectionUtilities.PickPrintSet(base.Document);
				sheets = TitleBlockSelectionUtilities.GetSheetsFromPrintSet(selectedPrintSet);
			}
			else
			{
				sheets = TitleBlockSelectionUtilities.GetSheetsFromSelectedSchedules(base.UiDocument, base.Document);
			}
			if (sheets.Count == 0)
			{
				TaskDialog.Show("CheckListTitleBlocks", "Print Set không chứa sheet hợp lệ.");
				return;
			}
			List<ElementId> titleBlockIds = TitleBlockSelectionUtilities.CollectTitleBlockIds(base.Document, sheets);
			if (titleBlockIds.Count == 0)
			{
				TaskDialog.Show("CheckListTitleBlocks", "Không tìm thấy titleblock trên các sheet được chọn.");
				return;
			}
			base.UiDocument.Selection.SetElementIds(titleBlockIds);
			List<string> typeCountLines = TitleBlockSelectionUtilities.BuildTitleBlockTypeCountLines(base.Document, titleBlockIds);
			List<string> sheetUsageLines = TitleBlockSelectionUtilities.BuildSheetTitleBlockUsageLines(base.Document, sheets);
			string header = $"Số sheet: {sheets.Count} | Số titleblock: {titleBlockIds.Count}";
			TitleBlockSelectionUtilities.ShowTitleBlockTypeCountWindow(header, typeCountLines, sheetUsageLines);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (System.OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			TaskDialog.Show("CheckListTitleBlocks", ex3.ToString());
		}
	}
}
