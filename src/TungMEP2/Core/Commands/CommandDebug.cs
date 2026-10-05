using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace VP_RevitAPI.Commands;

[Transaction(TransactionMode.Manual)]
public class CommandDebug : ExternalCommand
{
	public override void Execute()
	{
		try
		{
			List<ViewSheetSet> sets = new FilteredElementCollector(base.Document).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().ToList();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Gỡ lỗi", ex2.ToString());
		}
	}
}
