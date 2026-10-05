using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Internal.Combine;

internal static class SetWorksetRunner
{
	internal static void Run(UIDocument uiDocument, Document document)
	{
		Element element = null;
		ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
		if (selectedIds.Count > 0)
		{
			element = document.GetElement(selectedIds.First());
		}
		else
		{
			Reference pickedReference = uiDocument.Selection.PickObject(ObjectType.Element, "Chon 1 phan tu");
			element = document.GetElement(pickedReference);
		}
		if (element == null || !document.IsWorkshared)
		{
			return;
		}
		WorksetId worksetId = element.WorksetId;
		using Transaction transaction = new Transaction(document, "Set Active Workset");
		transaction.Start();
		document.GetWorksetTable().SetActiveWorksetId(worksetId);
		transaction.Commit();
	}
}
