using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Helper;

public static class TTList
{
	public static List<object> SortByKey(this List<object> list1, List<double> list2)
	{
		var combined = list1.Zip(list2, (object first, double second) => new
		{
			First = first,
			Second = second
		});
		var sorted = combined.OrderBy(x => x.Second);
		return sorted.Select(x => x.First).ToList();
	}
}
