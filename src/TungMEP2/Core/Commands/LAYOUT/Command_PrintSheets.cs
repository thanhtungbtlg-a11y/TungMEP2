extern alias LegacyPrintSheets;

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using LegacyCommand = LegacyPrintSheets::TungMEP2.PrintSheets.Command_PrintSheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Command_PrintSheets : IExternalCommand
{
	public Result Execute(
		ExternalCommandData commandData,
		ref string message,
		ElementSet elements)
	{
		try
		{
			// Use the 2026.10.03.1 manual-STT maintenance engine, derived from
			// the verified 01/08 backup and shipped beside TungMEP2.
			// Do not roll back the shared helpers used by the other MEP tools.
			return new LegacyCommand().Execute(commandData, ref message, elements);
		}
		catch (Exception exception)
		{
			message = exception.Message;
			return Result.Failed;
		}
	}
}
