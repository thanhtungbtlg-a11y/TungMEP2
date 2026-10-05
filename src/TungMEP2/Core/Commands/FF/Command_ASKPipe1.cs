using System;
using System.Collections.Generic;
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
public class Command_ASKPipe1 : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<FamilyInstance> sprinklers = CollectSelectedSprinklers();
			if (sprinklers.Count == 0)
			{
				TaskDialog.Show("ASKPipe1", "Vui long pre-select cac Sprinkler can ket noi truoc khi chay lenh.");
				return;
			}
			Reference pipeReference = base.UiDocument.Selection.PickObject(ObjectType.PointOnElement, new Isf.Pipe(), "Pick point 1 tren Pipe sample");
			XYZ point2 = base.UiDocument.Selection.PickPoint("Pick point 2 de tao huong ASKPipe1");
			string error;
			if (!(base.Document.GetElement(pipeReference) is Pipe pipeSample))
			{
				TaskDialog.Show("ASKPipe1", "Khong lay duoc Pipe sample hop le.");
			}
			else if (!ASKPipe1Runner.TryRun(base.Document, sprinklers, pipeSample, pipeReference.GlobalPoint, point2, out error))
			{
				TaskDialog.Show("ASKPipe1", error);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("ASKPipe1", ex2.Message);
		}
	}

	private List<FamilyInstance> CollectSelectedSprinklers()
	{
		List<FamilyInstance> result = new List<FamilyInstance>();
		ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
		foreach (ElementId selectedId in selectedIds)
		{
			Element element = base.Document.GetElement(selectedId);
			if (element is FamilyInstance familyInstance)
			{
				Category category = familyInstance.Category;
				if (category != null && category.Id?.Value == -2008099)
				{
					result.Add(familyInstance);
				}
			}
		}
		return result;
	}
}
