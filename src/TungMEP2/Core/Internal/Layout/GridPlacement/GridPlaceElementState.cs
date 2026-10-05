using Autodesk.Revit.DB;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Internal.Layout.GridPlacement;

internal static class GridPlaceElementState
{
	internal const string TemplateTypeIdKey = "GridPlace.TemplateTypeId";

	internal const string TemplateZKey = "GridPlace.TemplateZ";

	internal const string TemplateLevelIdKey = "GridPlace.TemplateLevelId";

	internal const string SpacingMillimetersKey = "GridPlace.SpacingMillimeters";

	private const double DefaultSpacingMillimeters = 1000.0;

	internal static double GetSpacingMillimetersOrDefault()
	{
		double spacing;
		return (TryGetSpacingMillimeters(out spacing) && spacing > 0.0) ? spacing : 1000.0;
	}

	internal static bool TryGetSpacingMillimeters(out double spacing)
	{
		spacing = 0.0;
		if (!StaticStore.Bag.TryGetValue("GridPlace.SpacingMillimeters", out object value) || value == null)
		{
			return false;
		}
		if (!(value is double number) || number <= 0.0)
		{
			return false;
		}
		spacing = number;
		return true;
	}

	internal static bool TryGetTemplateTypeId(out ElementId templateTypeId)
	{
		templateTypeId = ElementId.InvalidElementId;
		if (!StaticStore.Bag.TryGetValue("GridPlace.TemplateTypeId", out object value) || value == null)
		{
			return false;
		}
		if (value is ElementId elementId && elementId != ElementId.InvalidElementId)
		{
			templateTypeId = elementId;
			return true;
		}
		return false;
	}

	internal static bool TryGetTemplateZ(out double templateZ)
	{
		templateZ = 0.0;
		if (!StaticStore.Bag.TryGetValue("GridPlace.TemplateZ", out object value) || value == null)
		{
			return false;
		}
		if (!(value is double number) || 1 == 0)
		{
			return false;
		}
		templateZ = number;
		return true;
	}

	internal static bool TryGetTemplateLevelId(out ElementId templateLevelId)
	{
		templateLevelId = ElementId.InvalidElementId;
		if (!StaticStore.Bag.TryGetValue("GridPlace.TemplateLevelId", out object value) || value == null)
		{
			return false;
		}
		if (value is ElementId levelId)
		{
			templateLevelId = levelId;
			return true;
		}
		return false;
	}

	internal static void Save(ElementId templateTypeId, double templateZ, ElementId templateLevelId, double spacingMillimeters)
	{
		StaticStore.Bag["GridPlace.TemplateTypeId"] = templateTypeId;
		StaticStore.Bag["GridPlace.TemplateZ"] = templateZ;
		StaticStore.Bag["GridPlace.TemplateLevelId"] = templateLevelId;
		StaticStore.Bag["GridPlace.SpacingMillimeters"] = spacingMillimeters;
	}
}
