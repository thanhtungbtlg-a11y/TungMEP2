using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowDown : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Down;

	protected override string TransactionName => "Elbow Down";

	protected override string DialogTitle => "Elbow Down";
}
