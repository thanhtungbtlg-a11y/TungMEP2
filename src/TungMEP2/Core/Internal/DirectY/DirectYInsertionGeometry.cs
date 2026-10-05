using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYInsertionGeometry
{
    internal static void AppendMeasuredRunProbes(IList<double> distances, double low, double high)
    {
        if (distances == null || !DirectYGuards.Finite(low) || !DirectYGuards.Finite(high) || low < 0 || low > high) return;
        // Dedupe only numerical equality, not geometric tolerance: an invalid
        // canned point can lie very close to a narrow valid measured window.
        double epsilon = 1e-12 * Math.Max(1, Math.Max(Math.Abs(low), Math.Abs(high)));
        foreach (double distance in new[] { low + (high - low) / 2, low, high })
            if (distances.Count < 20 && !distances.Any(value => Math.Abs(value - distance) <= epsilon)) distances.Add(distance);
    }

    internal static bool TryInitialRunBounds(double length, double diameter, double minimumLength, double clearance,
        bool perpendicularTee, out double margin)
    {
        margin = 0;
        if (!Positive(length) || !Positive(diameter) || !Positive(minimumLength) || !Positive(clearance)) return false;
        margin = perpendicularTee ? minimumLength + clearance : Math.Max(minimumLength * 2, diameter * 2);
        return DirectYGuards.Finite(margin) && length > 2 * margin;
    }

    internal static bool TryMeasuredRunBounds(double length, double minimumLength, double firstOffset, double lastOffset,
        double margin, out double low, out double high, out double requiredLength)
    {
        low = high = requiredLength = 0;
        if (!Positive(length) || !Positive(minimumLength) || !Positive(margin) ||
            !DirectYGuards.Finite(firstOffset) || !DirectYGuards.Finite(lastOffset) || firstOffset >= lastOffset) return false;
        double safeLength = Math.Max(minimumLength, margin);
        low = Math.Max(margin, safeLength - firstOffset);
        double endReserve = Math.Max(margin, safeLength + lastOffset);
        high = length - endReserve;
        requiredLength = low + endReserve;
        if (!DirectYGuards.Finite(low) || !DirectYGuards.Finite(high) || !DirectYGuards.Finite(requiredLength))
        {
            low = high = requiredLength = 0;
            return false;
        }
        // Millimetres converted to Revit feet can round a zero-width window
        // slightly inside-out. Normalize numerical equality only; keep the
        // actual minimum Pipe length and geometric clearance unchanged.
        double epsilon = 1e-12 * Math.Max(1, Math.Max(Math.Abs(length), Math.Max(Math.Abs(low), Math.Abs(high))));
        if (low > high && low - high <= epsilon) high = low;
        return low <= high;
    }

    internal static IReadOnlyList<double> ElevationSteps(double step, double maximum)
    {
        var result = new List<double>();
        if (!DirectYGuards.Finite(step) || !DirectYGuards.Finite(maximum) || step <= 0 || maximum < 0) return result;
        double increments = Math.Floor(maximum / step);
        // Reject an impractical request; never silently shorten the requested elevation range.
        if (!DirectYGuards.Finite(increments) || increments > 10000) return result;
        result.Add(0);
        for (int i = 1; i <= (int)increments; i++) { result.Add(i * step); result.Add(-i * step); }
        return result;
    }

    internal static IReadOnlyList<Vector3d> BranchDirections(Vector3d main, Vector3d toward, Vector3d anchor, Vector3d source,
        bool perpendicularTee = false)
    {
        var result = new List<Vector3d>();
        if (!Normalize(main, out var u) || !Normalize(anchor, out var a) || !Normalize(source, out var d)) return result;
        Vector3d radial = toward - u * toward.Dot(u);
        if (!Normalize(radial, out var r))
        { radial = a - u * a.Dot(u); if (!Normalize(radial, out r)) Normalize(u.Cross(Math.Abs(u.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0)), out r); }
        Vector3d s = u.Cross(r);
        double axial = perpendicularTee ? 0 : Math.Sqrt(0.5), radialLength = perpendicularTee ? 1 : Math.Sqrt(0.5);
        foreach (double sign in perpendicularTee ? new[] { 1.0 } : new[] { 1.0, -1.0 })
        {
            double x = a.Dot(r), y = a.Dot(s), amplitude = Math.Sqrt(x * x + y * y), wanted = (-a.Dot(d) - sign * axial * a.Dot(u)) / radialLength;
            if (amplitude > 1e-9 && Math.Abs(wanted) <= amplitude + 1e-9)
            {
                double phase = Math.Atan2(y, x), delta = Math.Acos(Math.Max(-1, Math.Min(1, wanted / amplitude)));
                foreach (double angle in new[] { phase + delta, phase - delta }) result.Add(u * (sign * axial) + (r * Math.Cos(angle) + s * Math.Sin(angle)) * radialLength);
            }
            foreach (double angle in new[] { 0.0, Math.PI / 4, -Math.PI / 4, Math.PI / 2, -Math.PI / 2, Math.PI })
                result.Add(u * (sign * axial) + (r * Math.Cos(angle) + s * Math.Sin(angle)) * radialLength);
        }
        var unique = new List<Vector3d>();
        foreach (var direction in result.OrderByDescending(value => value.Dot(r)))
            if (!unique.Any(value => (value - direction).Length < 1e-7)) unique.Add(direction);
        return unique;
    }

    internal static bool TryFitSingle(Vector3d pivot, Vector3d anchor, Vector3d sourceOpen, Vector3d sourceDirection,
        Vector3d target, Vector3d targetOpen, Vector3d lineAxis, double minShift, double maxShift, double anchorLength,
        double sourceLength, double minLength, double maxAdjustment, double tolerance, out double shift, double? fixedAnchorSlide = null)
    {
        shift = 0;
        if (!Normalize(anchor, out var a) || !Normalize(sourceDirection, out var d) || !Normalize(-targetOpen, out var m) ||
            !Normalize(lineAxis, out var u) || !pivot.IsFinite || !sourceOpen.IsFinite || !target.IsFinite ||
            !DirectYGuards.Finite(minShift) || !DirectYGuards.Finite(maxShift) || minShift > maxShift ||
            !Positive(anchorLength) || !Positive(sourceLength) || !Positive(minLength) || !Positive(maxAdjustment) || !Positive(tolerance) ||
            Math.Abs(a.Dot(d) - a.Dot(m)) > 1e-6) return false;
        Vector3d dr = d - a * d.Dot(a), mr = m - a * m.Dot(a);
        if (dr.Length < 1e-9 || mr.Length < 1e-9) return false;
        double angle = Math.Atan2(a.Dot(dr.Cross(mr)), dr.Dot(mr));
        Vector3d rotated = pivot + DirectYArticulation.Rotate(sourceOpen - pivot, a, angle);
        double low = Math.Max(-maxAdjustment, minLength - anchorLength + 1e-8), high = maxAdjustment;
        if (fixedAnchorSlide.HasValue)
        {
            if (!DirectYGuards.Finite(fixedAnchorSlide.Value) || fixedAnchorSlide.Value < low || fixedAnchorSlide.Value > high) return false;
            low = fixedAnchorSlide.Value; high = fixedAnchorSlide.Value;
        }
        var options = DirectYArticulation.SolveLinear(target - rotated, a, m, -u, tolerance,
            new[] { low, Math.Max(-maxAdjustment, minLength - sourceLength + 1e-8), minShift },
            new[] { high, maxAdjustment, maxShift }).Where(value =>
            (target + u * value.Z - rotated - a * value.X - m * value.Y).Length <= tolerance)
            .OrderBy(value => value.X * value.X + value.Y * value.Y + 0.01 * value.Z * value.Z).ToArray();
        if (options.Length == 0) return false;
        shift = options[0].Z; return true;
    }

    // With parallel anchor/run axes the direction cone permits every roll. Solve the
    // remaining coplanarity equation analytically, including eccentric fitting offsets.
    internal static IReadOnlyList<double> SinglePositionRolls(Vector3d pivot, Vector3d anchor, Vector3d sourceOpen,
        Vector3d sourceDirection, Vector3d main, Vector3d center, Vector3d referenceDirection, Vector3d referenceOffset,
        double minShift, double maxShift, double anchorLength, double sourceLength, double minLength,
        double maxAdjustment, double tolerance, double? fixedAnchorSlide = null)
    {
        var roots = new List<double>();
        if (!Normalize(main, out var u) || !Normalize(anchor, out var a) || !Normalize(sourceDirection, out var d) ||
            !Normalize(referenceDirection, out var open) || !pivot.IsFinite || !sourceOpen.IsFinite || !center.IsFinite ||
            !referenceOffset.IsFinite || !Positive(tolerance) || Math.Abs(a.Dot(u)) < 1 - 1e-10 ||
            Math.Abs(a.Dot(d) + a.Dot(open)) > 1e-6 || Math.Abs(open.Dot(u)) > 1e-6) return roots;
        double f0 = Measure(0), f90 = Measure(Math.PI / 2), f180 = Measure(Math.PI);
        double constant = (f0 + f180) / 2, cosine = f0 - constant, sine = f90 - constant;
        double amplitude = Math.Sqrt(cosine * cosine + sine * sine);
        if (!DirectYGuards.Finite(amplitude) || !DirectYGuards.Finite(constant)) return roots;
        if (amplitude <= 1e-10)
        {
            if (Math.Abs(constant) <= tolerance) Accept(0);
        }
        else if (Math.Abs(constant) <= amplitude + tolerance)
        {
            double phase = Math.Atan2(sine, cosine);
            double delta = Math.Acos(Math.Max(-1, Math.Min(1, -constant / amplitude)));
            Accept(phase + delta); Accept(phase - delta);
        }
        return roots.OrderBy(Math.Abs).ToArray();

        double Measure(double roll)
        {
            Vector3d m = -DirectYArticulation.Rotate(open, u, roll);
            Vector3d dr = d - a * d.Dot(a), mr = m - a * m.Dot(a);
            double rotation = Math.Atan2(a.Dot(dr.Cross(mr)), dr.Dot(mr));
            Vector3d rotated = pivot + DirectYArticulation.Rotate(sourceOpen - pivot, a, rotation);
            Vector3d normal = a.Cross(m);
            return (center + DirectYArticulation.Rotate(referenceOffset, u, roll) - rotated).Dot(normal) / normal.Length;
        }
        void Accept(double roll)
        {
            roll = Math.Atan2(Math.Sin(roll), Math.Cos(roll));
            if (!TryFitSingle(pivot, a, sourceOpen, d, center + DirectYArticulation.Rotate(referenceOffset, u, roll),
                DirectYArticulation.Rotate(open, u, roll), u, minShift, maxShift, anchorLength, sourceLength,
                minLength, maxAdjustment, tolerance, out _, fixedAnchorSlide)) return;
            if (!roots.Any(value => Math.Abs(Math.Atan2(Math.Sin(value - roll), Math.Cos(value - roll))) < 1e-7)) roots.Add(roll);
        }
    }

    // Refine roots of the actual positional residual, rather than using only canned Y roll angles.
    internal static IReadOnlyList<double> PositionRolls(DirectYArticulationInput input, Vector3d main, Vector3d center,
        Vector3d referenceDirection, Vector3d referenceOffset, double height)
    {
        var roots = new List<double>();
        if (input == null || !Normalize(main, out var axis) || !center.IsFinite || !referenceOffset.IsFinite ||
            !Normalize(referenceDirection, out var direction) || !DirectYGuards.Finite(height)) return roots;
        // 720 intervals over a full turn: 0.5deg discovery, followed by continuous root refinement.
        const int divisions = 720;
        double step = 2 * Math.PI / divisions;
        var samples = new IReadOnlyList<double>[divisions + 1];
        for (int i = 0; i <= divisions; i++)
        {
            double angle = -Math.PI + i * step;
            samples[i] = Measure(angle);
            if (samples[i].Any(value => DirectYGuards.Finite(value) && Math.Abs(value) <= input.PositionTolerance)) Accept(angle);
        }
        int count = samples.Max(sample => sample.Count);
        for (int index = 0; index < count; index++)
            for (int i = 1; i <= divisions; i++)
            {
                double left = -Math.PI + (i - 1) * step, right = left + step;
                double a = Value(samples[i - 1], index), b = Value(samples[i], index);
                if (DirectYGuards.Finite(a) && DirectYGuards.Finite(b) && a * b < 0)
                {
                    for (int iteration = 0; iteration < 48; iteration++)
                    {
                        double middle = (left + right) / 2, value = Value(Measure(middle), index);
                        if (!DirectYGuards.Finite(value)) break;
                        if (Math.Abs(value) < input.PositionTolerance * 0.01) { left = right = middle; break; }
                        if (a * value <= 0) { right = middle; b = value; } else { left = middle; a = value; }
                    }
                    Accept((left + right) / 2);
                }
                // Tangent roots do not change sign; refine local minima of the absolute residual.
                if (i < divisions && DirectYGuards.Finite(b) && Math.Abs(b) > input.PositionTolerance && Math.Abs(b) <= Math.Abs(a) &&
                    Math.Abs(b) <= Math.Abs(Value(samples[i + 1], index)))
                {
                    left = -Math.PI + (i - 1) * step; right = left + 2 * step;
                    for (int iteration = 0; iteration < 48; iteration++)
                    {
                        double p = left + (right - left) * 0.381966011250105, q = left + (right - left) * 0.618033988749895;
                        double fp = Math.Abs(Value(Measure(p), index)), fq = Math.Abs(Value(Measure(q), index));
                        if (!DirectYGuards.Finite(fp) || !DirectYGuards.Finite(fq)) break;
                        if (fp < fq) right = q; else left = p;
                    }
                    Accept((left + right) / 2);
                }
            }
        return roots.OrderBy(Math.Abs).ToArray();

        IReadOnlyList<double> Measure(double angle)
        {
            input.TargetPoint = center + DirectYArticulation.Rotate(referenceOffset, axis, angle);
            input.TargetOpenDirection = DirectYArticulation.Rotate(direction, axis, angle);
            return DirectYArticulation.HeightGaps(input, height);
        }
        void Accept(double angle)
        {
            Measure(angle);
            if (!DirectYArticulation.HeightSolutions(input, height).Any()) return;
            angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
            if (!roots.Any(value => Math.Abs(Math.Atan2(Math.Sin(value - angle), Math.Cos(value - angle))) < 1e-5)) roots.Add(angle);
        }
        static double Value(IReadOnlyList<double> values, int index) => index < values.Count ? values[index] : double.NaN;
    }
    private static bool Positive(double value) => value > 0 && DirectYGuards.Finite(value);
    private static bool Normalize(Vector3d value, out Vector3d unit)
    { unit = default; if (!value.IsFinite || value.Length < 1e-10 || !DirectYGuards.Finite(value.Length)) return false; unit = value * (1 / value.Length); return true; }
}
