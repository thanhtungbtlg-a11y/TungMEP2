using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Helper;

public static class SheetSelectionResolver
{
	public static IList<long> ResolveSheetIds(IList<long> selectedSheetIds, IList<long> sheetIdsFromSchedules)
	{
		if (selectedSheetIds.Count > 0)
		{
			return selectedSheetIds.Distinct().ToList();
		}
		return sheetIdsFromSchedules.Distinct().ToList();
	}
}
