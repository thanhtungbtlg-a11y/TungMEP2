using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class TitleBlockTypeOption
{
	public ElementId Id { get; }

	public string Name { get; }

	public TitleBlockTypeOption(ElementId id, string name)
	{
		Id = id;
		Name = (string.IsNullOrWhiteSpace(name) ? "(Không rõ Type)" : name.Trim());
	}
}
