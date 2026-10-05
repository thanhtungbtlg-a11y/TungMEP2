using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowUp : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Up;

	protected override string TransactionName => "Elbow Up";

	protected override string DialogTitle => "Elbow Up";
}
