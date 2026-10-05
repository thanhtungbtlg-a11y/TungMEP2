using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Electrical;

namespace VP_RevitAPI.Commands.Electrical;

[Transaction(TransactionMode.Manual)]
public class Command_RemoveCircuits : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<Element> selectedElements = GetSelectionOrPick();
			if (selectedElements.Count == 0)
			{
				return;
			}
			List<Element> removableCircuitElements = selectedElements.Where(delegate(Element e)
			{
				Category category = e.Category;
				return category != null && category.Id.IntegerValue == -2008037;
			}).ToList();
			using Transaction transaction = new Transaction(base.Document, "Remove Circuits");
			transaction.Start();
			int removedLoads = 0;
			foreach (Element element in selectedElements)
			{
				ISet<ElectricalSystem> electricalSystemSet = ElectricalSystemUtils.GetElectricalSystems(element);
				ConnectorSet connectorSet = ElectricalSystemUtils.GetConnectorSet(element);
				if (electricalSystemSet == null || connectorSet == null)
				{
					continue;
				}
				foreach (ElectricalSystem electricalSystem in electricalSystemSet)
				{
					try
					{
						electricalSystem?.Remove(connectorSet);
						removedLoads++;
					}
					catch
					{
					}
				}
			}
			int deletedCircuits = 0;
			if (removableCircuitElements.Count > 0)
			{
				deletedCircuits = removableCircuitElements.Count;
				base.Document.Delete(removableCircuitElements.Select((Element i) => i.Id).ToList());
			}
			transaction.Commit();
			TaskDialog.Show("Remove Circuits", $"Removed items: {removedLoads}\nDeleted circuits: {deletedCircuits}");
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Remove Circuits", ex2.ToString());
		}
	}

	private List<Element> GetSelectionOrPick()
	{
		ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
		if (selectedIds.Count > 0)
		{
			return (from id in selectedIds
				select base.Document.GetElement(id) into e
				where e != null
				select e).ToList();
		}
		IList<Reference> references = base.UiDocument.Selection.PickObjects(ObjectType.Element, new CircuitedElementFilter(), "Select circuited elements or panel");
		return (from reference in references
			select base.Document.GetElement(reference.ElementId) into e
			where e != null
			select e).ToList();
	}
}
