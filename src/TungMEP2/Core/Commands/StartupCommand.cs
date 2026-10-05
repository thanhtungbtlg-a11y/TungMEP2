using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace VP_RevitAPI.Commands;

[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
	public override void Execute()
	{
		try
		{
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Khởi động", ex2.ToString());
		}
	}
}
