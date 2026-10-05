using System;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Common;

internal static class MepPipeBodyConnectionGeometry
{
	private const double Epsilon = 1E-09;

	internal static bool TryProjectInteriorPoint(
		XYZ start,
		XYZ end,
		XYZ source,
		double endClearance,
		out XYZ projectedPoint)
	{
		projectedPoint = null;
		if (start == null || end == null || source == null || endClearance < 0.0)
		{
			return false;
		}

		XYZ axis = end - start;
		double lengthSquared = axis.DotProduct(axis);
		if (lengthSquared < Epsilon)
		{
			return false;
		}

		double parameter = (source - start).DotProduct(axis) / lengthSquared;
		if (parameter <= 0.0 || parameter >= 1.0)
		{
			return false;
		}

		XYZ candidate = start + axis.Multiply(parameter);
		if (candidate.DistanceTo(start) < endClearance ||
		    candidate.DistanceTo(end) < endClearance)
		{
			return false;
		}

		projectedPoint = candidate;
		return true;
	}

	internal static bool AreParallel(
		XYZ firstDirection,
		XYZ secondDirection,
		double cosineTolerance = 0.999)
	{
		if (firstDirection == null || secondDirection == null ||
		    firstDirection.GetLength() < Epsilon ||
		    secondDirection.GetLength() < Epsilon)
		{
			return true;
		}

		double cosine = Math.Abs(firstDirection.Normalize()
			.DotProduct(secondDirection.Normalize()));
		return cosine >= cosineTolerance;
	}
}
