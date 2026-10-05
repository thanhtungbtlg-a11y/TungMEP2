using System;

namespace VP_RevitAPI.Internal.Layout.GridPlacement;

internal static class GridPlacementMath
{
	internal static int CalculateCount(double axisLength, double spacing)
	{
		if (spacing <= 0.0)
		{
			throw new ArgumentOutOfRangeException("spacing", "Spacing must be > 0.");
		}
		return Math.Max(1, (int)Math.Floor(Math.Abs(axisLength) / spacing) + 1);
	}

	internal static double GetDirectionSign(double value)
	{
		return (value < 0.0) ? (-1.0) : 1.0;
	}
}
