using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal static class PipeSpacingGeometry
{
	private const double Epsilon = 1E-09;

	internal static bool TryBuildAxes(
		IReadOnlyList<XYZ> directions,
		XYZ viewRight,
		XYZ viewUp,
		double parallelDotTolerance,
		out XYZ runAxis,
		out XYZ spacingAxis,
		out string error)
	{
		runAxis = null;
		spacingAxis = null;
		error = string.Empty;
		if (directions == null || directions.Count < 2 ||
		    viewRight == null || viewUp == null)
		{
			error = "Không đủ hướng Pipe/view để xác định trục dàn.";
			return false;
		}

		List<XYZ> planDirections = new List<XYZ>(directions.Count);
		XYZ first = null;
		foreach (XYZ direction in directions)
		{
			XYZ plan = ToPlan(direction);
			if (plan.GetLength() <= Epsilon)
			{
				error = "Pipe đứng hoặc gần đứng không được hỗ trợ trong PipeSpacing.";
				return false;
			}
			plan = plan.Normalize();
			if (first == null)
			{
				first = plan;
			}
			else if (plan.DotProduct(first) < 0.0)
			{
				plan = plan.Negate();
			}
			planDirections.Add(plan);
		}

		XYZ sum = planDirections.Aggregate(XYZ.Zero, (current, direction) => current + direction);
		if (sum.GetLength() <= Epsilon)
		{
			error = "Các hướng Pipe triệt tiêu nhau nên không xác định được hướng chạy chung.";
			return false;
		}
		runAxis = sum.Normalize();
		double requiredDot = Math.Max(0.0, Math.Min(1.0, parallelDotTolerance));
		XYZ resolvedRunAxis = runAxis;
		if (planDirections.Any(direction => Math.Abs(direction.DotProduct(resolvedRunAxis)) < requiredDot))
		{
			error = "Các Pipe đã chọn không song song trong dung sai cho phép.";
			runAxis = null;
			return false;
		}

		XYZ candidate = XYZ.BasisZ.CrossProduct(runAxis);
		if (candidate.GetLength() <= Epsilon)
		{
			error = "Không xác định được phương dàn ngang.";
			runAxis = null;
			return false;
		}
		candidate = candidate.Normalize();

		XYZ rightPlan = ToPlan(viewRight);
		XYZ upPlan = ToPlan(viewUp);
		if (rightPlan.GetLength() <= Epsilon || upPlan.GetLength() <= Epsilon)
		{
			error = "Active view không có trục Right/Up ngang hợp lệ.";
			runAxis = null;
			return false;
		}
		rightPlan = rightPlan.Normalize();
		upPlan = upPlan.Normalize();
		XYZ preferredPositive = Math.Abs(runAxis.DotProduct(rightPlan)) >=
		                        Math.Abs(runAxis.DotProduct(upPlan))
			? upPlan
			: rightPlan;
		if (candidate.DotProduct(preferredPositive) < 0.0)
		{
			candidate = candidate.Negate();
		}

		spacingAxis = candidate;
		return true;
	}

	internal static double ProjectExtent(BoundingBoxXYZ box, XYZ axis)
	{
		if (box == null || axis == null || axis.GetLength() <= Epsilon)
		{
			return 0.0;
		}
		XYZ unitAxis = axis.Normalize();
		Transform transform = box.Transform ?? Transform.Identity;
		double minimum = double.PositiveInfinity;
		double maximum = double.NegativeInfinity;
		for (int x = 0; x <= 1; x++)
		{
			for (int y = 0; y <= 1; y++)
			{
				for (int z = 0; z <= 1; z++)
				{
					XYZ local = new XYZ(
						x == 0 ? box.Min.X : box.Max.X,
						y == 0 ? box.Min.Y : box.Max.Y,
						z == 0 ? box.Min.Z : box.Max.Z);
					double coordinate = transform.OfPoint(local).DotProduct(unitAxis);
					minimum = Math.Min(minimum, coordinate);
					maximum = Math.Max(maximum, coordinate);
				}
			}
		}
		return double.IsInfinity(minimum) ? 0.0 : Math.Max(0.0, maximum - minimum);
	}

	internal static XYZ Midpoint(Line line)
	{
		return (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;
	}

	private static XYZ ToPlan(XYZ value)
	{
		return value == null ? XYZ.Zero : new XYZ(value.X, value.Y, 0.0);
	}
}
