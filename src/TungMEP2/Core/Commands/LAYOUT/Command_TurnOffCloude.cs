using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Layout.Sheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_TurnOffCloude : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			TurnOffCloudeRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (System.OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			TaskDialog.Show("TurnOffCloude", ex3.Message);
		}
	}
}
