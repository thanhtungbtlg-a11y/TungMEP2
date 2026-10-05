using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn.FitSpace;
using System;
using System.Diagnostics;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_FitSpace : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			FitSpaceRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
			// Silent cancellation from Revit selection.
		}
		catch (Exception exception)
		{
			Debug.WriteLine(exception);
			TaskDialog.Show("FitSpace", "FitSpace không thể hoàn tất: " + exception.Message);
		}
	}
}
