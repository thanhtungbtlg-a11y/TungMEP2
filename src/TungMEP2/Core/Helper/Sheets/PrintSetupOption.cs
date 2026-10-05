using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class PrintSetupOption
{
	public ElementId Id { get; }

	public string Name { get; }

	public bool IsActive { get; }

	public bool IsInSession { get; }

	public PrintSetupOption(ElementId id, string name, bool isActive, bool isInSession)
	{
		Id = id;
		Name = name;
		IsActive = isActive;
		IsInSession = isInSession;
	}
}
