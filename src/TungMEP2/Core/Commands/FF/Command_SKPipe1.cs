using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;
using VP_RevitAPI.Internal.FF;

namespace VP_RevitAPI.Commands.FF;

[Transaction(TransactionMode.Manual)]
public class Command_SKPipe1 : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			Reference sprinklerReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.Sprinkler(), "Chon 1 sprinkler (SK)");
			Reference pipeReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chon 1 pipe chinh");
			FamilyInstance sprinkler = base.Document.GetElement(sprinklerReference) as FamilyInstance;
			Pipe mainPipe = base.Document.GetElement(pipeReference) as Pipe;
			string error;
			if (sprinkler == null || mainPipe == null)
			{
				TaskDialog.Show("SKPipe1", "Khong lay duoc SK hoac Pipe hop le.");
			}
			else if (!SKConnectPipeRunner.TryConnect(base.Document, sprinkler, mainPipe, out error))
			{
				TaskDialog.Show("SKPipe1", error);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("SKPipe1", ex2.Message);
		}
	}
}
