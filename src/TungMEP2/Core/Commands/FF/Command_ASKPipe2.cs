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
public class Command_ASKPipe2 : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<FamilyInstance> sprinklers = CollectSelectedSprinklers();
			if (sprinklers.Count == 0)
			{
				TaskDialog.Show("ASKPipe2", "Vui long pre-select cac Sprinkler can ket noi truoc khi chay lenh.");
				return;
			}
			Reference pipeReference = base.UiDocument.Selection.PickObject(ObjectType.PointOnElement, new Isf.Pipe(), "Pick point on element tren Pipe sample");
			if (!(base.Document.GetElement(pipeReference) is Pipe pipeSample))
			{
				TaskDialog.Show("ASKPipe2", "Khong lay duoc Pipe sample hop le.");
				return;
			}
			FarthestConnectionMode? mode = ResolveFarthestConnectionMode();
			if (!mode.HasValue)
			{
				return;
			}
			FamilyInstance standardSprinkler = null;
			if (ASKPipe2Runner.TryRun(base.Document, sprinklers, pipeSample, pipeReference.GlobalPoint, mode.Value, standardSprinkler, out bool requireStandardSelection, out string error))
			{
				return;
			}
			if (requireStandardSelection)
			{
				standardSprinkler = PickStandardSprinkler();
				if (standardSprinkler != null && !ASKPipe2Runner.TryRun(base.Document, sprinklers, pipeSample, pipeReference.GlobalPoint, mode.Value, standardSprinkler, out bool _, out error))
				{
					TaskDialog.Show("ASKPipe2", error);
				}
			}
			else
			{
				TaskDialog.Show("ASKPipe2", error);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("ASKPipe2", ex2.Message);
		}
	}

	private FarthestConnectionMode? ResolveFarthestConnectionMode()
	{
		TaskDialog dialog = new TaskDialog("ASKPipe2")
		{
			MainInstruction = "Ket noi SK xa nhat theo kieu nao?",
			MainContent = "Chon Tee de keo trunk vuot 300mm. Chon Elbow de ket thuc trunk tai SK xa nhat.",
			CommonButtons = TaskDialogCommonButtons.Cancel,
			AllowCancellation = true
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tee");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Elbow");
		TaskDialogResult result = dialog.Show();
		if (1 == 0)
		{
		}
		FarthestConnectionMode? result2 = result switch
		{
			TaskDialogResult.CommandLink1 => FarthestConnectionMode.Tee, 
			TaskDialogResult.CommandLink2 => FarthestConnectionMode.Elbow, 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		return result2;
	}

	private FamilyInstance? PickStandardSprinkler()
	{
		Reference sprinklerReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.Sprinkler(), "Chon 1 sprinkler lam chuan cho ASKPipe2");
		return base.Document.GetElement(sprinklerReference) as FamilyInstance;
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
