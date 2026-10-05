using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Helper.Electrical;

internal sealed class LevelFilter : ISelectionFilter
{
	public bool AllowElement(Element elem)
	{
		if (elem.Category == null)
		{
			return false;
		}
		return elem.Category.Id.IntegerValue == -2000240 || elem.Category.Id.IntegerValue == -2006020;
	}

	public bool AllowReference(Reference reference, XYZ position)
	{
		return false;
	}
}
