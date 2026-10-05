using System;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

/// <summary>
/// Geometry-only helpers shared by the independent MEP alignment commands.
/// Keeping these calculations separate makes them testable without changing a model.
/// </summary>
internal static class MepAlignmentGeometry
{
	private const double Epsilon = 1E-09;

	internal static bool TryComputeMoveToIntersect(
		XYZ fixedOrigin,
		XYZ fixedDirection,
		XYZ movingOrigin,
		XYZ movingDirection,
		out XYZ moveVector,
		out XYZ intersectionPoint)
	{
		moveVector = XYZ.Zero;
		intersectionPoint = XYZ.Zero;
		if (!TryNormalize(fixedDirection, out XYZ fixedUnit) ||
		    !TryNormalize(movingDirection, out XYZ movingUnit))
		{
			return false;
		}

		double dot = fixedUnit.DotProduct(movingUnit);
		double denominator = 1.0 - dot * dot;
		if (Math.Abs(denominator) < Epsilon)
		{
			return false;
		}

		XYZ originDelta = fixedOrigin - movingOrigin;
		double fixedProjection = fixedUnit.DotProduct(originDelta);
		double movingProjection = movingUnit.DotProduct(originDelta);
		double fixedParameter = (dot * movingProjection - fixedProjection) / denominator;
		double movingParameter = (movingProjection - dot * fixedProjection) / denominator;
		XYZ pointOnFixed = fixedOrigin + fixedUnit.Multiply(fixedParameter);
		XYZ pointOnMoving = movingOrigin + movingUnit.Multiply(movingParameter);

		moveVector = pointOnFixed - pointOnMoving;
		intersectionPoint = pointOnFixed;
		return true;
	}

	internal static bool TryBuildPerpendicularSameSlopeDirection(
		XYZ mainDirection,
		XYZ currentBranchDirection,
		out XYZ targetDirection)
	{
		targetDirection = XYZ.Zero;
		if (!TryNormalize(mainDirection, out XYZ mainUnit) ||
		    !TryNormalize(currentBranchDirection, out XYZ branchUnit))
		{
			return false;
		}

		double mainHorizontalLength = Math.Sqrt(mainUnit.X * mainUnit.X + mainUnit.Y * mainUnit.Y);
		if (mainHorizontalLength < Epsilon)
		{
			return false;
		}

		XYZ mainPlanDirection = new XYZ(
			mainUnit.X / mainHorizontalLength,
			mainUnit.Y / mainHorizontalLength,
			0.0);
		XYZ perpendicularPlanDirection = new XYZ(
			-mainPlanDirection.Y,
			mainPlanDirection.X,
			0.0);
		double slopeMagnitude = Math.Abs(mainUnit.Z / mainHorizontalLength);

		XYZ firstSlopeLine = (perpendicularPlanDirection + XYZ.BasisZ.Multiply(slopeMagnitude)).Normalize();
		XYZ secondSlopeLine = (perpendicularPlanDirection - XYZ.BasisZ.Multiply(slopeMagnitude)).Normalize();
		double firstScore = Math.Abs(firstSlopeLine.DotProduct(branchUnit));
		double secondScore = Math.Abs(secondSlopeLine.DotProduct(branchUnit));
		XYZ best = firstScore >= secondScore ? firstSlopeLine : secondSlopeLine;

		if (best.DotProduct(branchUnit) < 0.0)
		{
			best = best.Negate();
		}

		targetDirection = best;
		return true;
	}

	internal static XYZ ComputeAxisAlignmentMove(XYZ fixedOrigin, XYZ movingOrigin, XYZ fixedAxis)
	{
		if (!TryNormalize(fixedAxis, out XYZ axis))
		{
			return XYZ.Zero;
		}

		XYZ delta = fixedOrigin - movingOrigin;
		return delta - axis.Multiply(delta.DotProduct(axis));
	}

	internal static double ComputeSignedAngle(XYZ fromDirection, XYZ toDirection, XYZ axisDirection)
	{
		if (!TryNormalize(fromDirection, out XYZ fromUnit) ||
		    !TryNormalize(toDirection, out XYZ toUnit) ||
		    !TryNormalize(axisDirection, out XYZ axisUnit))
		{
			return 0.0;
		}

		double sine = axisUnit.DotProduct(fromUnit.CrossProduct(toUnit));
		double cosine = Math.Max(-1.0, Math.Min(1.0, fromUnit.DotProduct(toUnit)));
		return Math.Atan2(sine, cosine);
	}

	private static bool TryNormalize(XYZ vector, out XYZ normalized)
	{
		normalized = XYZ.Zero;
		if (vector == null || vector.GetLength() < Epsilon)
		{
			return false;
		}

		normalized = vector.Normalize();
		return true;
	}
}
