using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowUp45 : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Up45;

	protected override string TransactionName => "Elbow Up 45 Degrees";

	protected override string DialogTitle => "Elbow Up 45";
}
