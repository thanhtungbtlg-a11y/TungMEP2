using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets.AlignViewports;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_AlignViewports : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<ViewSheet> selectedSheets = GetSelectedSheets();
			if (selectedSheets.Count < 2)
			{
				TaskDialog.Show("Align Viewports", "Select at least two sheets in Project Browser and run again.");
				return;
			}
			UI_AlignViewportsWindow window = new UI_AlignViewportsWindow(selectedSheets);
			if (window.ShowDialog() == true && window.MainSheet != null && window.Options != null)
			{
				List<ViewSheet> otherSheets = selectedSheets.Where((ViewSheet sheet) => sheet.Id != window.MainSheet.Id).ToList();
				AlignViewportsReport report = ViewportAligner.Align(base.Document, window.MainSheet, otherSheets, window.Options);
				TaskDialog.Show("Align Viewports", report.ToDisplayText());
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Align Viewports", ex2.ToString());
		}
	}

	private List<ViewSheet> GetSelectedSheets()
	{
		return (from sheet in (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id)).OfType<ViewSheet>()
			where !sheet.IsPlaceholder
			group sheet by sheet.Id into @group
			select @group.First() into sheet
			orderby sheet.SheetNumber
			select sheet).ToList();
	}
}
