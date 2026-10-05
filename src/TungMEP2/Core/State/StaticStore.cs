using System.Collections.Generic;

namespace VP_RevitAPI.State;

public static class StaticStore
{
	public static List<double> DataElbowLength { get; } = new List<double>();

	public static Dictionary<string, object?> Bag { get; } = new Dictionary<string, object>();

	public static void ClearAll()
	{
		DataElbowLength.Clear();
		Bag.Clear();
	}
}
