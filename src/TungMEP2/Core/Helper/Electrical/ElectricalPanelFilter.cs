using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Helper.Electrical;

internal sealed class ElectricalPanelFilter : ISelectionFilter
{
	private readonly FamilyInstance _panel;

	private readonly List<ElementId> _canCircuit = new List<ElementId>();

	public ElectricalPanelFilter(FamilyInstance panel)
	{
		_panel = panel;
	}

	public bool AllowElement(Element elem)
	{
		if (_canCircuit.Contains(elem.Id))
		{
			return true;
		}
		if (!(elem is FamilyInstance familyInstance))
		{
			return false;
		}
		ISet<ElectricalSystem> electricalSystems = familyInstance.MEPModel?.GetElectricalSystems();
		if (electricalSystems == null)
		{
			return false;
		}
		foreach (ElectricalSystem electricalSystem in electricalSystems)
		{
			try
			{
				electricalSystem.SelectPanel(_panel);
				_canCircuit.Add(elem.Id);
				return true;
			}
			catch
			{
			}
		}
		return false;
	}

	public bool AllowReference(Reference reference, XYZ position)
	{
		return false;
	}
}
