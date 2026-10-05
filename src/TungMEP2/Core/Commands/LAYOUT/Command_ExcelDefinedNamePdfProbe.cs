using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_ExcelDefinedNamePdfProbe : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			UI_ExcelLegendLinkSyncWindow window = new UI_ExcelLegendLinkSyncWindow(base.Document, ElementId.InvalidElementId);
			window.ShowDialog();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Excel PDF Probe", ex2.Message);
		}
	}
}
