using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class DwgExportSetupOption
{
	public ElementId Id { get; }

	public string Name { get; }

	public bool IsActive { get; }

	public DwgExportSetupOption(ElementId id, string name, bool isActive)
	{
		Id = id;
		Name = name;
		IsActive = isActive;
	}
}
