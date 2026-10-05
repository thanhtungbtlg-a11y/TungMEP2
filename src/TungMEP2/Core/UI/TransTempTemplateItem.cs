using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class TransTempTemplateItem
{
	public ElementId Id { get; }

	public string Name { get; }

	public TransTempTemplateItem(ElementId id, string name)
	{
		Id = id;
		Name = (name ?? string.Empty).Trim();
	}
}
