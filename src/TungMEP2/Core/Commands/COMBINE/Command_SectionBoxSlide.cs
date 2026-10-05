using Autodesk.Revit.Attributes;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Combine;

namespace VP_RevitAPI.Commands.COMBINE;

[Transaction(TransactionMode.Manual)]
public class Command_SectionBoxSlide : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		SectionBoxSlideRunner.Run();
	}
}
