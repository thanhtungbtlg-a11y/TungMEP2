using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Layout.GridPlacement;

internal static class GridPlaceElementSettingsRunner
{
	private sealed class FamilyInstancePointFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return element is FamilyInstance familyInstance && familyInstance.Location is LocationPoint;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Reference templateReference = uiDocument.Selection.PickObject(ObjectType.Element, new FamilyInstancePointFilter(), "Chọn FamilyInstance mẫu có điểm đặt.");
		if (!(document.GetElement(templateReference) is FamilyInstance template))
		{
			throw new InvalidOperationException("Không lấy được FamilyInstance mẫu.");
		}
		if (!(template.Location is LocationPoint))
		{
			throw new InvalidOperationException("FamilyInstance mẫu phải có LocationPoint.");
		}
		if (template.Symbol == null)
		{
			throw new InvalidOperationException("FamilyInstance mẫu không có Family Type.");
		}
		double currentSpacing = GridPlaceElementState.GetSpacingMillimetersOrDefault();
		UI_GridPlaceElementSettingsWindow window = new UI_GridPlaceElementSettingsWindow(currentSpacing, BuildTemplateLabel(template));
		if (window.ShowDialog() == true)
		{
			LocationPoint locationPoint = (LocationPoint)template.Location;
			ElementId templateLevelId = template.LevelId;
			GridPlaceElementState.Save(template.Symbol.Id, locationPoint.Point.Z, templateLevelId, window.SpacingMillimeters);
		}
	}

	private static string BuildTemplateLabel(FamilyInstance familyInstance)
	{
		string family = familyInstance.Symbol?.FamilyName ?? "(No Family)";
		string type = familyInstance.Symbol?.Name ?? "(No Type)";
		return $"{family} - {type} (TypeId: {familyInstance.Symbol?.Id.Value ?? (-1)})";
	}
}
