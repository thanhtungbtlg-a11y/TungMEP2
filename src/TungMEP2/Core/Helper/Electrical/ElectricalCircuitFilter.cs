using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Helper.Electrical;

internal sealed class ElectricalCircuitFilter : ISelectionFilter
{
	private readonly ElectricalSystem _electricalSystem;

	private readonly List<ElementId> _canCircuit = new List<ElementId>();

	public ElectricalCircuitFilter(ElectricalSystem electricalSystem)
	{
		_electricalSystem = electricalSystem;
	}

	public bool AllowElement(Element elem)
	{
		if (_canCircuit.Contains(elem.Id))
		{
			return true;
		}
		ElementSet elementSet = new ElementSet();
		elementSet.Insert(elem);
		if (_electricalSystem.AddToCircuit(elementSet))
		{
			_canCircuit.Add(elem.Id);
			_electricalSystem.RemoveFromCircuit(elementSet);
			return true;
		}
		return false;
	}

	public bool AllowReference(Reference reference, XYZ position)
	{
		return false;
	}
}
