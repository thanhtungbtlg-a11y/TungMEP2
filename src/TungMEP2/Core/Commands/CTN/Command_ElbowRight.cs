using Autodesk.Revit.Attributes;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_ElbowRight : Command_ElbowRunBase
{
	protected override ElbowRunDirection Direction => ElbowRunDirection.Right;

	protected override string TransactionName => "Elbow Right";

	protected override string DialogTitle => "Elbow Right";
}
