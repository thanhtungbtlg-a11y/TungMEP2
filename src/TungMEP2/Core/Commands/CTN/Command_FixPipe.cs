using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_FixPipe : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			FixPipeRunner.Run(base.UiDocument, base.Document);
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}
}
