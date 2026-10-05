using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowLeft : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Left;

	protected override string TransactionName => "Elbow Left";

	protected override string DialogTitle => "Elbow Left";
}
