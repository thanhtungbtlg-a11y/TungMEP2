using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Helper.Electrical;

internal sealed class LinkedCategoryFilter : ISelectionFilter
{
	private readonly Category _category;

	private RevitLinkInstance? _currentInstance;

	public LinkedCategoryFilter(Category category)
	{
		_category = category;
	}

	public bool AllowElement(Element e)
	{
		_currentInstance = e as RevitLinkInstance;
		return _currentInstance != null;
	}

	public bool AllowReference(Reference refer, XYZ point)
	{
		if (_currentInstance == null)
		{
			return false;
		}
		return _currentInstance.GetLinkDocument().GetElement(refer.LinkedElementId)?.Category?.Id == _category.Id;
	}
}
