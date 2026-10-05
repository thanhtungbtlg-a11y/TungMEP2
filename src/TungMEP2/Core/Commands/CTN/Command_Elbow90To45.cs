using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_Elbow90To45 : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ICollection<ElementId> selectionIds = base.UiDocument.Selection.GetElementIds();
			List<FamilyInstance> elbows = selectionIds.Select(base.Document.GetElement).OfType<FamilyInstance>().ToList();
			if (elbows.Count == 0)
			{
				Reference pickRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.FamilyInstanceOnly(), "Chọn Family Instance");
				FamilyInstance elbow = (FamilyInstance)base.Document.GetElement(pickRef);
				elbows.Add(elbow);
			}
			using Transaction tx = new Transaction(base.Document, "Elbow90to45");
			tx.Start();
			foreach (FamilyInstance elbow2 in elbows)
			{
				PipeUtilities.Elbow90To45(elbow2);
			}
			tx.Commit();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("CreateElbow", ex2.ToString());
		}
	}
}
