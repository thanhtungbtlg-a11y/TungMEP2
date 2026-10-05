using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Helper.Electrical;

internal sealed class CircuitedElementFilter : ISelectionFilter
{
	public bool AllowElement(Element elem)
	{
		if (!(elem is FamilyInstance { MEPModel: var mepModel }))
		{
			return false;
		}
		if (mepModel == null)
		{
			return false;
		}
		return mepModel.GetElectricalSystems() != null || mepModel.GetType() == typeof(ElectricalEquipment);
	}

	public bool AllowReference(Reference reference, XYZ position)
	{
		return false;
	}
}
