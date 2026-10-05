using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class SheetChoiceItem
{
	public ViewSheet Sheet { get; }

	public bool IsMain { get; set; }

	public string DisplayName => Sheet.SheetNumber + " - " + Sheet.Name;

	public SheetChoiceItem(ViewSheet sheet, bool isMain)
	{
		Sheet = sheet;
		IsMain = isMain;
	}
}
