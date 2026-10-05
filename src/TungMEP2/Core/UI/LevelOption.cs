using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class LevelOption
{
	public ElementId Id { get; }

	public string Name { get; }

	public double Elevation { get; }

	public string DisplayName => $"{Name} ({Elevation:0.###})";

	public LevelOption(ElementId id, string name, double elevation)
	{
		Id = id;
		Name = (string.IsNullOrWhiteSpace(name) ? "(Unnamed Level)" : name.Trim());
		Elevation = elevation;
	}
}
