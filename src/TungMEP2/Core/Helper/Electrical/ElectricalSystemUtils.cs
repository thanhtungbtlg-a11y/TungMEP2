using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace VP_RevitAPI.Helper.Electrical;

internal static class ElectricalSystemUtils
{
	internal static bool IsElectricalEquipment(Element element)
	{
		if (element.Category == null)
		{
			return false;
		}
		return element.Category.Id.IntegerValue == -2001040;
	}

	internal static ISet<ElectricalSystem>? GetElectricalSystems(Element element)
	{
		if (element is ElectricalSystem electricalSystem)
		{
			return new HashSet<ElectricalSystem> { electricalSystem };
		}
		if (element is FamilyInstance familyInstance)
		{
			return familyInstance.MEPModel?.GetElectricalSystems();
		}
		return null;
	}

	internal static ConnectorSet? GetConnectorSet(Element element)
	{
		if (element is FamilyInstance familyInstance)
		{
			return familyInstance.MEPModel?.ConnectorManager?.Connectors;
		}
		if (element is MEPCurve mepCurve)
		{
			return mepCurve.ConnectorManager?.Connectors;
		}
		return null;
	}

	internal static FamilyInstance? GetBaseEquipment(ElectricalSystem electricalSystem)
	{
		return electricalSystem?.BaseEquipment;
	}

	internal static List<ElectricalSystem> GetCircuitsByPanel(Document doc, ElementId panelId)
	{
		return new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>().Where(delegate(ElectricalSystem es)
		{
			FamilyInstance baseEquipment = GetBaseEquipment(es);
			return baseEquipment != null && baseEquipment.Id == panelId;
		})
			.ToList();
	}

	internal static List<ElementId> GetCircuitElementIds(ElectricalSystem electricalSystem)
	{
		return (from Element e in electricalSystem?.Elements
			select e.Id).ToList() ?? new List<ElementId>();
	}
}
