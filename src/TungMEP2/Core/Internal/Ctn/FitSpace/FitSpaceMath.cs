using System;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceMath
{
	internal const double MinimumIncludedAngleDegrees = 15.0;
	internal const double MaximumIncludedAngleDegrees = 165.0;
	internal const double MiddleLengthFactor = 2.2;

	internal static bool TryCalculate(
		FitSpaceScalarInput input,
		out FitSpaceScalarTarget target,
		out string error)
	{
		target = default;
		error = string.Empty;
		if (!IsFinite(input.ReferenceSize) || input.ReferenceSize <= 0.0)
		{
			error = "DN tham chiếu của cụm elbow không hợp lệ.";
			return false;
		}
		if (!IsFinite(input.IncludedAngleRadians))
		{
			error = "Góc giữa hai Pipe ngoài không hợp lệ.";
			return false;
		}

		double minimum = DegreesToRadians(MinimumIncludedAngleDegrees);
		double maximum = DegreesToRadians(MaximumIncludedAngleDegrees);
		if (input.IncludedAngleRadians < minimum || input.IncludedAngleRadians > maximum)
		{
			error = $"Góc giữa hai Pipe ngoài phải nằm trong khoảng {MinimumIncludedAngleDegrees:0}°–{MaximumIncludedAngleDegrees:0}°.";
			return false;
		}

		double rawMiddleLength = input.ReferenceSize * MiddleLengthFactor;
		double denominator = 2.0 * Math.Sin(input.IncludedAngleRadians / 2.0);
		if (!IsFinite(denominator) || denominator <= 0.0)
		{
			error = "Không tính được khoảng đặt hai elbow từ góc đã chọn.";
			return false;
		}

		target = new FitSpaceScalarTarget(rawMiddleLength, rawMiddleLength / denominator);
		return true;
	}

	internal static bool IsNormalized(
		double currentStandoff,
		double targetStandoff,
		double tolerance)
	{
		return IsFinite(currentStandoff) &&
		       IsFinite(targetStandoff) &&
		       IsFinite(tolerance) &&
		       tolerance >= 0.0 &&
		       Math.Abs(currentStandoff - targetStandoff) <= tolerance;
	}

	internal static bool IsConnectorAxisOpposed(
		double dotProduct,
		double maximumDeviationDegrees)
	{
		return IsFinite(dotProduct) &&
		       IsFinite(maximumDeviationDegrees) &&
		       maximumDeviationDegrees >= 0.0 &&
		       ConnectorAxisDeviationDegrees(dotProduct) <= maximumDeviationDegrees;
	}

	internal static double ConnectorAxisDeviationDegrees(double dotProduct)
	{
		if (!IsFinite(dotProduct))
		{
			return double.PositiveInfinity;
		}

		double clamped = Math.Max(-1.0, Math.Min(1.0, dotProduct));
		return Math.Acos(-clamped) * 180.0 / Math.PI;
	}

	private static double DegreesToRadians(double degrees)
	{
		return degrees * Math.PI / 180.0;
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
