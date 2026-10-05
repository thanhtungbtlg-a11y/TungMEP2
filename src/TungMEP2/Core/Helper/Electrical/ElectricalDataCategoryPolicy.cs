using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Electrical;

internal static class ElectricalDataCategoryPolicy
{
	private static readonly HashSet<long> AllowedCategoryIds = new HashSet<long> { -2008016L, -2008085L, -2001040L, -2001120L, -2008087L, -2008081L, -2008083L, -2008079L };

	internal static IReadOnlyCollection<long> GetAllowedCategoryIds()
	{
		return AllowedCategoryIds;
	}

	internal static bool IsAllowed(Element? element)
	{
		return element?.Category != null && IsAllowedCategoryId(element.Category.Id.Value);
	}

	internal static bool IsAllowedCategoryId(long categoryId)
	{
		return AllowedCategoryIds.Contains(categoryId);
	}
}
