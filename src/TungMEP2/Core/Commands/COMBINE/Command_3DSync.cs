using Autodesk.Revit.Attributes;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Combine;

namespace VP_RevitAPI.Commands.COMBINE;

[Transaction(TransactionMode.Manual)]
public class Command_3DSync : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		ThreeDSyncRunner.Run(base.UiApplication, base.UiDocument, base.Document);
	}
}
