using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn.DirectY;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_DirectY : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			DirectYRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
			// User cancelled selection.
		}
		catch (Exception exception)
		{
			Debug.WriteLine(exception);
			TaskDialog.Show("TungMEP2 - DirectY", "DirectY không thể hoàn tất: " + exception.Message);
		}
	}
}
