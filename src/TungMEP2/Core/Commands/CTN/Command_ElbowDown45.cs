using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowDown45 : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Down45;

	protected override string TransactionName => "Elbow Down 45 Degrees";

	protected override string DialogTitle => "Elbow Down 45";
}
