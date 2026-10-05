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
public class Command_CropLineWeight : SecuredExternalCommand
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
				TaskDialog.Show("CropLineWeight", "Không tìm thấy sheet hợp lệ để xử lý.");
				return;
			}
			List<View> views = CropBoxLineWeightUtilities.GetViewsPlacedOnSheets(base.Document, sheets);
			if (views.Count == 0)
			{
				TaskDialog.Show("CropLineWeight", "Không tìm thấy view nào đang đặt trên các sheet đã chọn.");
				return;
			}
			int? selectedLineWeight = CropBoxLineWeightUtilities.PromptLineWeight();
			if (selectedLineWeight.HasValue)
			{
				CropLineWeightReport report = CropBoxLineWeightUtilities.Apply(base.Document, views, selectedLineWeight.Value);
				TaskDialog.Show("CropLineWeight", report.ToDisplayText());
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("CropLineWeight", ex2.ToString());
		}
	}
}
