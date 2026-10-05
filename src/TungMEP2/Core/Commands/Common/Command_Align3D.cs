using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using VP_RevitAPI.Internal.Common;

namespace VP_RevitAPI.Commands.Common;

[Transaction(TransactionMode.Manual)]
public class Command_Align3D : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			Align3DRunner.Run(base.UiDocument, base.Document);
		}
		catch (OperationCanceledException)
		{
		}
	}
}
