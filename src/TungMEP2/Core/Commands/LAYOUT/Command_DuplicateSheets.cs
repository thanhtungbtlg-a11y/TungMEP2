using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets.DuplicateSheets;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_DuplicateSheets : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<ViewSheet> selectedSheets = GetSelectedSheets();
			if (selectedSheets.Count == 0)
			{
				TaskDialog.Show("Duplicate Sheets", "Select one or more sheets in Project Browser and run again.");
				return;
			}
			UI_DuplicateSheetsWindow window = new UI_DuplicateSheetsWindow(selectedSheets);
			if (window.ShowDialog() == true && window.Options != null)
			{
				SheetDuplicateReport report = SheetDuplicator.Duplicate(base.Document, selectedSheets, window.Options);
				TaskDialog.Show("Duplicate Sheets", report.ToDisplayText());
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Duplicate Sheets", ex2.ToString());
		}
	}

	private List<ViewSheet> GetSelectedSheets()
	{
		return (from sheet in (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id)).OfType<ViewSheet>()
			where !sheet.IsPlaceholder
			group sheet by sheet.Id into @group
			select @group.First()).ToList();
	}
}
