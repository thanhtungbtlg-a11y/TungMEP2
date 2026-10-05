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
public class Command_AddCir : SecuredExternalCommand
{
	private sealed class AddCirPickFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is FamilyInstance || elem is MEPCurve;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return false;
		}
	}

	protected override void ExecuteCore()
	{
		try
		{
			Reference sourceReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new CircuitedElementFilter(), "Pick source element in target circuit");
			Element sourceElement = base.Document.GetElement(sourceReference);
			ElectricalSystem targetCircuit = ResolveUniqueCircuit(sourceElement);
			if (targetCircuit == null)
			{
				TaskDialog.Show("Add Circuit", "Source element must belong to exactly one circuit.");
				return;
			}
			Reference addReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new AddCirPickFilter(), "Pick element to add to the selected circuit");
			Element addElement = base.Document.GetElement(addReference);
			if (addElement.Id == sourceElement.Id)
			{
				TaskDialog.Show("Add Circuit", "Source element and added element must be different.");
				return;
			}
			if (targetCircuit != null && targetCircuit.Elements.Cast<Element>().Any((Element element) => element.Id == addElement.Id))
			{
				TaskDialog.Show("Add Circuit", "Selected element is already in the target circuit.");
				return;
			}
			if (ElectricalSystemUtils.GetConnectorSet(addElement) == null)
			{
				TaskDialog.Show("Add Circuit", "Selected element has no connectors available for circuiting.");
				return;
			}
			using Transaction transaction = new Transaction(base.Document, "Add To Circuit");
			transaction.Start();
			ElementSet elementSet = new ElementSet();
			elementSet.Insert(addElement);
			bool added = targetCircuit.AddToCircuit(elementSet);
			transaction.Commit();
			if (!added)
			{
				TaskDialog.Show("Add Circuit", "Revit rejected the selected element for this circuit. Check connector type, system compatibility, and panel constraints.");
			}
			else
			{
				TaskDialog.Show("Add Circuit", $"Added element {addElement.Id.Value} to circuit {targetCircuit.CircuitNumber}.");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Add Circuit", ex2.ToString());
		}
	}

	private static ElectricalSystem? ResolveUniqueCircuit(Element element)
	{
		ISet<ElectricalSystem> systems = ElectricalSystemUtils.GetElectricalSystems(element);
		if (systems == null)
		{
			return null;
		}
		List<ElectricalSystem> circuits = systems.Distinct().ToList();
		if (circuits.Count != 1)
		{
			return null;
		}
		return circuits[0];
	}
}
