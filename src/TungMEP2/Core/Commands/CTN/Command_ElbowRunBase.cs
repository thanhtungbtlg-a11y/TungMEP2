using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public abstract class Command_ElbowRunBase : SecuredExternalCommand
{
	protected abstract ElbowRunDirection Direction { get; }

	protected abstract string TransactionName { get; }

	protected abstract string DialogTitle { get; }

	protected override void ExecuteCore()
	{
		try
		{
			using Transaction tx = new Transaction(base.Document, TransactionName);
			tx.Start();
			if (!ElbowRunService.Execute(base.UiDocument, base.Document, Direction, out string error))
			{
				tx.RollBack();
				if (!string.IsNullOrWhiteSpace(error))
				{
					TaskDialog.Show(DialogTitle, error);
				}
			}
			else
			{
				tx.Commit();
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show(DialogTitle, ex2.ToString());
		}
	}
}
