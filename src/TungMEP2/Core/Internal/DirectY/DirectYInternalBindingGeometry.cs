using System;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal readonly struct DirectYRigidStep
{
    internal DirectYRigidStep(Vector3d pivot, Vector3d axis, double angle, double slide)
    { Pivot=pivot; Axis=axis; Angle=angle; Slide=slide; }
    internal Vector3d Pivot { get; }
    internal Vector3d Axis { get; }
    internal double Angle { get; }
    internal double Slide { get; }
}

internal static class DirectYInternalBindingGeometry
{
    internal static bool MatchesFittingFrame(Vector3d currentX, Vector3d currentY, Vector3d expectedX,
        Vector3d expectedY, double angularTolerance) =>
        MatchesAxis(currentX, expectedX, angularTolerance) && MatchesAxis(currentY, expectedY, angularTolerance);
    internal static bool AllowsExistingOffset(bool originalInternal, bool unchanged, bool reciprocalUnique,
        bool matchedRoundSize, double originalGap, double axisDot, double coincidenceTolerance) =>
        originalInternal && unchanged && reciprocalUnique && matchedRoundSize &&
        DirectYGuards.Finite(originalGap) && DirectYGuards.Finite(axisDot) &&
        DirectYGuards.Finite(coincidenceTolerance) && coincidenceTolerance > 0 && originalGap > coincidenceTolerance &&
        axisDot >= -1.000000001 && axisDot <= -Math.Cos(5 * Math.PI / 180);
    internal static bool TryTransform(Vector3d point, Vector3d axis, DirectYRigidStep? outer, DirectYRigidStep inner,
        out Vector3d transformedPoint, out Vector3d transformedAxis)
    {
        transformedPoint=point; transformedAxis=axis;
        if (!point.IsFinite || !Normalize(axis,out transformedAxis)) return false;
        if (outer.HasValue && !Apply(outer.Value,ref transformedPoint,ref transformedAxis)) return false;
        return Apply(inner,ref transformedPoint,ref transformedAxis);
    }

    private static bool Apply(DirectYRigidStep step,ref Vector3d point,ref Vector3d axis)
    {
        if (!step.Pivot.IsFinite || !Normalize(step.Axis,out var rotationAxis) ||
            !DirectYGuards.Finite(step.Angle) || !DirectYGuards.Finite(step.Slide)) return false;
        point=step.Pivot + DirectYArticulation.Rotate(point-step.Pivot,rotationAxis,step.Angle) + rotationAxis*step.Slide;
        axis=DirectYArticulation.Rotate(axis,rotationAxis,step.Angle);
        return point.IsFinite && axis.IsFinite;
    }

    private static bool Normalize(Vector3d axis,out Vector3d normalized)
    {
        normalized=default;
        if (!axis.IsFinite || !DirectYGuards.Finite(axis.Length) || axis.Length<1e-10) return false;
        normalized=axis*(1/axis.Length); return true;
    }
    internal static bool MatchesPort(Vector3d currentPoint, Vector3d currentAxis, Vector3d expectedPoint,
        Vector3d expectedAxis, double positionTolerance, double angularTolerance) =>
        currentPoint.IsFinite && expectedPoint.IsFinite && DirectYGuards.Finite(positionTolerance) && positionTolerance>0 &&
        (currentPoint-expectedPoint).Length<=positionTolerance && MatchesAxis(currentAxis,expectedAxis,angularTolerance);
    private static bool MatchesAxis(Vector3d currentAxis, Vector3d expectedAxis, double angularTolerance) =>
        Normalize(currentAxis,out var current) && Normalize(expectedAxis,out var expected) &&
        DirectYGuards.Finite(angularTolerance) && angularTolerance>0 && angularTolerance<Math.PI &&
        current.Dot(expected)>=Math.Cos(angularTolerance);
    internal static bool MatchesOffset(Vector3d first, Vector3d second, Vector3d expectedFirst,
        Vector3d expectedSecond, double tolerance) =>
        first.IsFinite && second.IsFinite && expectedFirst.IsFinite && expectedSecond.IsFinite &&
        DirectYGuards.Finite(tolerance) && tolerance>0 && ((second-first)-(expectedSecond-expectedFirst)).Length<=tolerance;
}
