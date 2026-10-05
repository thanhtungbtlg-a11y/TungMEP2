using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class AutoTagPipeTagTypeItem
{
	public ElementId Id { get; }

	public string DisplayName { get; }

	public AutoTagPipeTagTypeItem(ElementId id, string displayName)
	{
		Id = id;
		DisplayName = displayName;
	}
}
