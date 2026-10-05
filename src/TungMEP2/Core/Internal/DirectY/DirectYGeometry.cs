using System;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal readonly struct Vector3d
{
	internal Vector3d(double x, double y, double z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	internal double X { get; }
	internal double Y { get; }
	internal double Z { get; }

	internal double Length => Math.Sqrt(Dot(this));

	internal bool IsFinite =>
		!double.IsNaN(X) && !double.IsInfinity(X) &&
		!double.IsNaN(Y) && !double.IsInfinity(Y) &&
		!double.IsNaN(Z) && !double.IsInfinity(Z);

	internal double Dot(Vector3d other)
	{
		return X * other.X + Y * other.Y + Z * other.Z;
	}

	internal Vector3d Cross(Vector3d other)
	{
		return new Vector3d(
			Y * other.Z - Z * other.Y,
			Z * other.X - X * other.Z,
			X * other.Y - Y * other.X);
	}

	public static Vector3d operator +(Vector3d left, Vector3d right)
	{
		return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
	}

	public static Vector3d operator -(Vector3d left, Vector3d right)
	{
		return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
	}

	public static Vector3d operator -(Vector3d value)
	{
		return new Vector3d(-value.X, -value.Y, -value.Z);
	}

	public static Vector3d operator *(Vector3d value, double scalar)
	{
		return new Vector3d(value.X * scalar, value.Y * scalar, value.Z * scalar);
	}
}

internal readonly struct DirectYGeometryInput
{
	internal DirectYGeometryInput(
		Vector3d rotationPivot,
		Vector3d anchorAxisDirection,
		Vector3d sourceOpenPoint,
		Vector3d sourceAxisDirection,
		Vector3d targetConnectorPoint,
		Vector3d targetOpenDirection,
		double directionToleranceRadians,
		double positionTolerance)
	{
		RotationPivot = rotationPivot;
		AnchorAxisDirection = anchorAxisDirection;
		SourceOpenPoint = sourceOpenPoint;
		SourceAxisDirection = sourceAxisDirection;
		TargetConnectorPoint = targetConnectorPoint;
		TargetOpenDirection = targetOpenDirection;
		DirectionToleranceRadians = directionToleranceRadians;
		PositionTolerance = positionTolerance;
	}

	internal Vector3d RotationPivot { get; }
	internal Vector3d AnchorAxisDirection { get; }
	internal Vector3d SourceOpenPoint { get; }
	internal Vector3d SourceAxisDirection { get; }
	internal Vector3d TargetConnectorPoint { get; }
	internal Vector3d TargetOpenDirection { get; }
	internal double DirectionToleranceRadians { get; }
	internal double PositionTolerance { get; }
}

internal readonly struct DirectYGeometrySolution
{
	internal DirectYGeometrySolution(
		double rotationRadians,
		double axialSlide,
		double sourceExtension,
		Vector3d rotatedSourceOpenPoint,
		Vector3d rotatedSourceDirection,
		double residualDistance)
	{
		RotationRadians = rotationRadians;
		AxialSlide = axialSlide;
		SourceExtension = sourceExtension;
		RotatedSourceOpenPoint = rotatedSourceOpenPoint;
		RotatedSourceDirection = rotatedSourceDirection;
		ResidualDistance = residualDistance;
	}

	internal double RotationRadians { get; }
	internal double AxialSlide { get; }
	internal double SourceExtension { get; }
	internal Vector3d RotatedSourceOpenPoint { get; }
	internal Vector3d RotatedSourceDirection { get; }
	internal double ResidualDistance { get; }
}

internal readonly struct DirectYBranchClassification
{
	internal DirectYBranchClassification(int mainFirstIndex, int mainSecondIndex, int branchIndex)
	{
		MainFirstIndex = mainFirstIndex;
		MainSecondIndex = mainSecondIndex;
		BranchIndex = branchIndex;
	}

	internal int MainFirstIndex { get; }
	internal int MainSecondIndex { get; }
	internal int BranchIndex { get; }
}

internal static class DirectYGeometry
{
	private const double MinimumDirectionLength = 1e-12;
	private const double MinimumIndependentAxisSine = 1e-6;

	internal static bool TryClassifyOpenBranch(
		Vector3d axis0,
		Vector3d axis1,
		Vector3d axis2,
		int openPortIndex,
		double ambiguityToleranceRadians,
		out DirectYBranchClassification classification,
		out string error)
	{
		classification = default;
		error = string.Empty;
		if (openPortIndex < 0 || openPortIndex > 2 ||
			!IsFiniteNumber(ambiguityToleranceRadians) ||
			ambiguityToleranceRadians < 0 || ambiguityToleranceRadians > Math.PI)
		{
			error = "Open-port index or ambiguity tolerance is invalid.";
			return false;
		}

		if (!TryNormalize(axis0, out axis0) ||
			!TryNormalize(axis1, out axis1) ||
			!TryNormalize(axis2, out axis2))
		{
			error = "All three connector axes must be nonzero finite vectors.";
			return false;
		}

		// A pair's separation angle is greatest when its two outward connector axes
		// are most opposed. Compare angles rather than raw dot gaps so shallow branches
		// do not falsely appear ambiguous near 180 degrees.
		(int First, int Second, double Angle)[] pairs =
		{
			(0, 1, AxisSeparationRadians(axis0, axis1)),
			(0, 2, AxisSeparationRadians(axis0, axis2)),
			(1, 2, AxisSeparationRadians(axis1, axis2))
		};
		Array.Sort(pairs, (left, right) => right.Angle.CompareTo(left.Angle));
		if (pairs[0].Angle - pairs[1].Angle <= ambiguityToleranceRadians)
		{
			error = "The main-run connector pair is ambiguous.";
			return false;
		}

		int branchIndex = 3 - pairs[0].First - pairs[0].Second;
		if (openPortIndex != branchIndex)
		{
			error = "The sole open connector belongs to the Y main run, not its branch.";
			return false;
		}

		classification = new DirectYBranchClassification(
			pairs[0].First, pairs[0].Second, branchIndex);
		return true;
	}

	internal static bool TrySolve(
		DirectYGeometryInput input,
		out DirectYGeometrySolution solution,
		out string error)
	{
		solution = default;
		error = string.Empty;

		if (!IsFiniteNumber(input.DirectionToleranceRadians) ||
			input.DirectionToleranceRadians < 0 ||
			input.DirectionToleranceRadians > Math.PI ||
			!IsFiniteNumber(input.PositionTolerance) ||
			input.PositionTolerance < 0)
		{
			error = "Direction and position tolerances must be finite and nonnegative.";
			return false;
		}

		if (!input.RotationPivot.IsFinite ||
			!input.SourceOpenPoint.IsFinite ||
			!input.TargetConnectorPoint.IsFinite ||
			!TryNormalize(input.AnchorAxisDirection, out Vector3d anchorAxis) ||
			!TryNormalize(input.SourceAxisDirection, out Vector3d sourceAxis) ||
			!TryNormalize(input.TargetOpenDirection, out Vector3d targetOpenAxis))
		{
			error = "Points must be finite and directions must be nonzero finite vectors.";
			return false;
		}

		// The source direction runs from its connected elbow toward its open end.
		// Open connector axes face one another, so the desired source direction is opposite the target axis.
		Vector3d desiredSourceAxis = -targetOpenAxis;
		double sourceAxialComponent = sourceAxis.Dot(anchorAxis);
		double targetAxialComponent = desiredSourceAxis.Dot(anchorAxis);
		Vector3d sourceRadial = sourceAxis - anchorAxis * sourceAxialComponent;
		Vector3d targetRadial = desiredSourceAxis - anchorAxis * targetAxialComponent;
		if (sourceRadial.Length <= MinimumIndependentAxisSine ||
			targetRadial.Length <= MinimumIndependentAxisSine)
		{
			error = "Source and anchor axes must be nonparallel for a unique rotation and slide/extension solve.";
			return false;
		}

		double angle = Math.Atan2(
			anchorAxis.Dot(sourceRadial.Cross(targetRadial)),
			sourceRadial.Dot(targetRadial));
		Vector3d rotatedSourceAxis = RotateAboutUnitAxis(sourceAxis, anchorAxis, angle);
		double directionResidualRadians = Math.Atan2(
			rotatedSourceAxis.Cross(desiredSourceAxis).Length,
			rotatedSourceAxis.Dot(desiredSourceAxis));
		if (!IsFiniteNumber(directionResidualRadians) ||
			directionResidualRadians > input.DirectionToleranceRadians)
		{
			error = "Rotating about the anchor axis cannot make the open connector directions oppose within tolerance.";
			return false;
		}

		// Rotate the open point about the actual anchor-axis pivot, not the world origin.
		Vector3d rotatedOpenPoint = input.RotationPivot +
			RotateAboutUnitAxis(input.SourceOpenPoint - input.RotationPivot, anchorAxis, angle);
		Vector3d displacement = input.TargetConnectorPoint - rotatedOpenPoint;
		double cosine = anchorAxis.Dot(rotatedSourceAxis);
		double denominator = 1 - cosine * cosine;
		if (!rotatedOpenPoint.IsFinite || !displacement.IsFinite ||
			!IsFiniteNumber(denominator) ||
			denominator <= MinimumIndependentAxisSine * MinimumIndependentAxisSine)
		{
			error = "Rotated geometry is nonfinite or the slide and extension axes are indistinguishable.";
			return false;
		}

		// Solve D = s*anchorAxis + t*rotatedSourceAxis. t is signed; the caller
		// must check that the resulting physical source pipe length stays positive.
		double projectedAxialDisplacement = displacement.Dot(anchorAxis);
		double extension =
			(displacement.Dot(rotatedSourceAxis) - cosine * projectedAxialDisplacement) / denominator;
		double slide = projectedAxialDisplacement - cosine * extension;
		Vector3d residual = displacement - anchorAxis * slide - rotatedSourceAxis * extension;
		double residualDistance = residual.Length;
		if (!IsFiniteNumber(angle) || !rotatedSourceAxis.IsFinite ||
			!IsFiniteNumber(extension) || !IsFiniteNumber(slide) ||
			!IsFiniteNumber(residualDistance))
		{
			error = "The geometric solution is nonfinite.";
			return false;
		}

		if (residualDistance > input.PositionTolerance)
		{
			error = "The target connector is outside the plane spanned by slide and extension.";
			return false;
		}

		solution = new DirectYGeometrySolution(
			angle, slide, extension, rotatedOpenPoint, rotatedSourceAxis, residualDistance);
		return true;
	}

	private static bool TryNormalize(Vector3d value, out Vector3d unit)
	{
		unit = default;
		if (!value.IsFinite)
		{
			return false;
		}

		double length = value.Length;
		if (!IsFiniteNumber(length) || length <= MinimumDirectionLength)
		{
			return false;
		}

		unit = value * (1 / length);
		return true;
	}

	private static Vector3d RotateAboutUnitAxis(Vector3d value, Vector3d axis, double radians)
	{
		double cosine = Math.Cos(radians);
		double sine = Math.Sin(radians);
		return value * cosine + axis.Cross(value) * sine + axis * (axis.Dot(value) * (1 - cosine));
	}

	private static double AxisSeparationRadians(Vector3d first, Vector3d second)
	{
		return Math.Atan2(first.Cross(second).Length, first.Dot(second));
	}

	private static bool IsFiniteNumber(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
