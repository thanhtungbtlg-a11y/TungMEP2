using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal sealed class DirectYArticulationInput
{
    internal Vector3d UpstreamPivot, UpstreamAxis, AnchorPivot, AnchorAxis;
    internal Vector3d SourceOpen, SourceDirection, TargetPoint, TargetOpenDirection;
    internal double UpstreamLength, AnchorLength, SourceLength, MinimumLength, MaximumAdjustment, PositionTolerance;
    internal double? FixedUpstreamSlide;
    internal bool PreserveAnchorElevation;
    internal double? AnchorElevationOffset;
    internal Vector3d TargetSlideAxis;
    internal double TargetSlideMinimum, TargetSlideMaximum;
}

internal readonly struct DirectYArticulationSolution
{
    internal DirectYArticulationSolution(double outer, double inner, double upstreamSlide, double anchorSlide,
        double extension, Vector3d anchorPivot, Vector3d anchorAxis, Vector3d direction, double residual, double targetSlide = 0)
    {
        OuterRotation = outer; InnerRotation = inner; UpstreamSlide = upstreamSlide; AnchorSlide = anchorSlide;
        SourceExtension = extension; TransformedAnchorPivot = anchorPivot; TransformedAnchorAxis = anchorAxis;
        FinalSourceDirection = direction; Residual = residual;
        TargetSlide = targetSlide;
    }
    internal double OuterRotation { get; }
    internal double InnerRotation { get; }
    internal double UpstreamSlide { get; }
    internal double AnchorSlide { get; }
    internal double SourceExtension { get; }
    internal Vector3d TransformedAnchorPivot { get; }
    internal Vector3d TransformedAnchorAxis { get; }
    internal Vector3d FinalSourceDirection { get; }
    internal double Residual { get; }
    internal double TargetSlide { get; }
}

internal static class DirectYArticulation
{
    internal static bool TrySolve(DirectYArticulationInput input, out DirectYArticulationSolution solution, out string error)
        => TrySolveCore(input, out solution, out error, null);

    private static bool TrySolveCore(DirectYArticulationInput input, out DirectYArticulationSolution solution, out string error, List<double> gaps)
    {
        solution = default; error = string.Empty;
        if (input == null || !Normalize(input.UpstreamAxis, out var u) || !Normalize(input.AnchorAxis, out var a) ||
            !Normalize(input.SourceDirection, out var d) || !Normalize(-input.TargetOpenDirection, out var m) ||
            !input.UpstreamPivot.IsFinite || !input.AnchorPivot.IsFinite || !input.SourceOpen.IsFinite ||
            !input.TargetPoint.IsFinite || !PositiveFinite(input.MinimumLength) || !PositiveFinite(input.MaximumAdjustment) ||
            !PositiveFinite(input.PositionTolerance) || !PositiveFinite(input.UpstreamLength) ||
            !PositiveFinite(input.AnchorLength) || !PositiveFinite(input.SourceLength))
        {
            error = "Dữ liệu trục/cao độ của cụm không hợp lệ."; return false;
        }
        bool slideTarget = input.TargetSlideAxis.Length > 1e-10;
        if (!input.TargetSlideAxis.IsFinite || (input.AnchorElevationOffset.HasValue && !DirectYGuards.Finite(input.AnchorElevationOffset.Value)) || (input.FixedUpstreamSlide.HasValue &&
            (!DirectYGuards.Finite(input.FixedUpstreamSlide.Value) || Math.Abs(input.FixedUpstreamSlide.Value) > input.MaximumAdjustment)) ||
            (slideTarget && (!input.FixedUpstreamSlide.HasValue || !Normalize(input.TargetSlideAxis, out _) ||
            !DirectYGuards.Finite(input.TargetSlideMinimum) || !DirectYGuards.Finite(input.TargetSlideMaximum) ||
            input.TargetSlideMinimum > input.TargetSlideMaximum)))
        { error = "Ràng buộc cao độ/vị trí Y không hợp lệ."; return false; }
        Vector3d targetAxis = slideTarget ? input.TargetSlideAxis * (1 / input.TargetSlideAxis.Length) : default;
        double bendComponent = a.Dot(d);
        Vector3d radial = a - u * a.Dot(u);
        double x = m.Dot(radial), y = m.Dot(u.Cross(radial));
        double desired = bendComponent - a.Dot(u) * m.Dot(u);
        double amplitude = Math.Sqrt(x * x + y * y);
        List<double> outerAngles = new List<double>();
        if (amplitude < 1e-10)
        {
            if (Math.Abs(desired) > 1e-7) { error = "Góc hai elbow hiện có không thể đưa trục nhánh về hướng cổng Y."; return false; }
            outerAngles.Add(0);
            if (Math.Abs(Math.Abs(m.Dot(u)) - 1) < 1e-8 && radial.Length > 1e-8)
            {
                // Direction imposes no outer angle. The radial target offset supplies that constraint.
                Vector3d dr0 = d - a * d.Dot(a), mr0 = m - a * m.Dot(a);
                double inner0 = dr0.Length < 1e-10 || mr0.Length < 1e-10 ? 0 :
                    Math.Atan2(a.Dot(dr0.Cross(mr0)), dr0.Dot(mr0));
                Vector3d q0 = input.AnchorPivot + Rotate(input.SourceOpen - input.AnchorPivot, a, inner0) - input.UpstreamPivot;
                Vector3d t = input.TargetPoint - input.UpstreamPivot, normal = u.Cross(radial);
                double px = t.Dot(normal), py = t.Dot(u.Cross(normal)), required = normal.Dot(q0);
                double pa = Math.Sqrt(px * px + py * py);
                if (pa > 1e-10 && Math.Abs(required) <= pa + input.PositionTolerance)
                {
                    double phase = Math.Atan2(py, px), delta = Math.Acos(Math.Max(-1, Math.Min(1, required / pa)));
                    outerAngles.Add(Wrap(phase + delta)); outerAngles.Add(Wrap(phase - delta));
                }
            }
        }
        else
        {
            double cosine = desired / amplitude;
            if (cosine < -1 - 1e-7 || cosine > 1 + 1e-7)
            { error = "Góc hai elbow hiện có không thể đưa trục nhánh về hướng cổng Y."; return false; }
            double phase = Math.Atan2(y, x), delta = Math.Acos(Math.Max(-1, Math.Min(1, cosine)));
            outerAngles.Add(Wrap(phase + delta)); outerAngles.Add(Wrap(phase - delta));
        }
        double bestCost = double.PositiveInfinity;
        foreach (double outer in outerAngles)
        {
            Vector3d a1 = Rotate(a, u, outer), d1 = Rotate(d, u, outer);
            Vector3d p1 = input.UpstreamPivot + Rotate(input.AnchorPivot - input.UpstreamPivot, u, outer);
            Vector3d open1 = input.UpstreamPivot + Rotate(input.SourceOpen - input.UpstreamPivot, u, outer);
            Vector3d dr = d1 - a1 * d1.Dot(a1), mr = m - a1 * m.Dot(a1);
            double inner = dr.Length < 1e-10 || mr.Length < 1e-10 ? 0 :
                Math.Atan2(a1.Dot(dr.Cross(mr)), dr.Dot(mr));
            Vector3d finalDirection = Rotate(d1, a1, inner);
            if ((finalDirection - m).Length > 1e-6) continue;
            Vector3d open2 = p1 + Rotate(open1 - p1, a1, inner);
            Vector3d displacement = input.TargetPoint - open2;
            if (gaps != null && slideTarget && input.FixedUpstreamSlide.HasValue)
            {
                double sa = 0;
                bool locked = Math.Abs(a1.Z) > 1e-6 && (input.PreserveAnchorElevation || input.AnchorElevationOffset.HasValue);
                if (input.AnchorElevationOffset.HasValue && Math.Abs(a1.Z) > 1e-6) sa = input.AnchorElevationOffset.Value / a1.Z;
                if (input.AnchorElevationOffset.HasValue && Math.Abs(a1.Z) < 1e-6 && Math.Abs(input.AnchorElevationOffset.Value) > input.PositionTolerance)
                    gaps.Add(double.NaN);
                else if (locked)
                {
                    Vector3d normal = m.Cross(targetAxis);
                    gaps.Add(normal.Length < 1e-10 ? double.NaN : (displacement - u * input.FixedUpstreamSlide.Value - a1 * sa).Dot(normal) * (1 / normal.Length));
                }
                else
                {
                    Vector3d normal = m.Cross(targetAxis);
                    // Full rank has no position residual; bounds/lengths are checked by TrySolve.
                    gaps.Add(Math.Abs(a1.Dot(normal)) > 1e-8 ? 0 : normal.Length < 1e-10 ? double.NaN :
                        (displacement - u * input.FixedUpstreamSlide.Value).Dot(normal) * (1 / normal.Length));
                }
            }
            double[] lower = { Math.Max(-input.MaximumAdjustment,input.MinimumLength-input.UpstreamLength+1e-8),
                Math.Max(-input.MaximumAdjustment,input.MinimumLength-input.AnchorLength+1e-8),
                Math.Max(-input.MaximumAdjustment,input.MinimumLength-input.SourceLength+1e-8) };
            double[] upper = { input.MaximumAdjustment, input.MaximumAdjustment, input.MaximumAdjustment };
            // No hidden vertical translation on the inner anchor during a rotate-first height pass.
            if (input.AnchorElevationOffset.HasValue)
            {
                if (Math.Abs(a1.Z) < 1e-6) { if (Math.Abs(input.AnchorElevationOffset.Value) > input.PositionTolerance) continue; }
                else
                {
                    double slide = input.AnchorElevationOffset.Value / a1.Z;
                    lower[1] = Math.Max(lower[1], slide); upper[1] = Math.Min(upper[1], slide);
                    if (lower[1] > upper[1]) continue;
                }
            }
            else if (input.PreserveAnchorElevation && Math.Abs(a1.Z) > 1e-6) { lower[1] = 0; upper[1] = 0; }
            IEnumerable<Vector3d> options;
            if (slideTarget)
                options = SolveLinear(displacement - u * input.FixedUpstreamSlide.Value, a1, m, -targetAxis, input.PositionTolerance,
                    new[] { lower[1], lower[2], input.TargetSlideMinimum }, new[] { upper[1], upper[2], input.TargetSlideMaximum });
            else
            {
                if (input.FixedUpstreamSlide.HasValue)
                { lower[0] = Math.Max(lower[0], input.FixedUpstreamSlide.Value); upper[0] = Math.Min(upper[0], input.FixedUpstreamSlide.Value); }
                if (lower[0] > upper[0] || lower[1] > upper[1]) continue;
                options = SolveLinear(displacement, u, a1, m, input.PositionTolerance, lower, upper);
            }
            foreach (var moves in options)
            {
                double su = slideTarget ? input.FixedUpstreamSlide.Value : moves.X;
                double sa = slideTarget ? moves.X : moves.Y, extension = slideTarget ? moves.Y : moves.Z;
                double targetSlide = slideTarget ? moves.Z : 0;
                if (!moves.IsFinite) continue;
                if (input.UpstreamLength + su <= input.MinimumLength || input.AnchorLength + sa <= input.MinimumLength ||
                    input.SourceLength + extension <= input.MinimumLength || Math.Abs(su) > input.MaximumAdjustment ||
                    Math.Abs(sa) > input.MaximumAdjustment || Math.Abs(extension) > input.MaximumAdjustment) continue;
                double residual = (displacement + targetAxis * targetSlide - u * su - a1 * sa - m * extension).Length;
                if (!DirectYGuards.Finite(residual) || residual > input.PositionTolerance) continue;
                double cost = su * su + sa * sa + extension * extension + 0.1 * (outer * outer + inner * inner) + 0.01 * targetSlide * targetSlide;
                if (cost >= bestCost) continue;
                bestCost = cost;
                solution = new DirectYArticulationSolution(outer, inner, su, sa, extension, p1 + u * su, a1, finalDirection, residual, targetSlide);
            }
        }
        if (double.IsPositiveInfinity(bestCost))
        { error = "Không đủ chiều dài/cao độ để nối với các elbow hiện có; hoặc điểm nối vượt phạm vi chỉnh 5 m."; return false; }
        return true;
    }

    internal static IEnumerable<DirectYArticulationSolution> HeightSolutions(DirectYArticulationInput input, double height)
    {
        if (input == null || !DirectYGuards.Finite(height) || !Normalize(input.UpstreamAxis, out var u)) yield break;
        input.PreserveAnchorElevation = true; input.AnchorElevationOffset = null;
        if (Math.Abs(height) < 1e-9 || Math.Abs(u.Z) > 1e-6)
        {
            input.FixedUpstreamSlide = Math.Abs(height) < 1e-9 ? 0 : height / u.Z;
            if (TrySolve(input, out var upstream, out _)) yield return upstream;
        }
        if (Math.Abs(height) > 1e-9)
        {
            input.FixedUpstreamSlide = 0; input.AnchorElevationOffset = height;
            if (TrySolve(input, out var anchor, out _)) yield return anchor;
        }
    }

    internal static IReadOnlyList<double> HeightGaps(DirectYArticulationInput input, double height)
    {
        var result = new List<double>();
        if (input == null || !DirectYGuards.Finite(height) || !Normalize(input.UpstreamAxis, out var u)) return result;
        input.PreserveAnchorElevation = true; input.AnchorElevationOffset = null;
        if (Math.Abs(height) < 1e-9 || Math.Abs(u.Z) > 1e-6)
        {
            input.FixedUpstreamSlide = Math.Abs(height) < 1e-9 ? 0 : height / u.Z;
            TrySolveCore(input, out _, out _, result);
        }
        while (result.Count < 2) result.Add(double.NaN);
        if (Math.Abs(height) > 1e-9)
        {
            input.FixedUpstreamSlide = 0; input.AnchorElevationOffset = height;
            TrySolveCore(input, out _, out _, result);
        }
        return result;
    }

    internal static IEnumerable<Vector3d> SolveLinear(Vector3d delta, Vector3d a, Vector3d b, Vector3d c,
        double tolerance, double[] lower, double[] upper)
    {
        double determinant = a.Dot(b.Cross(c));
        if (Math.Abs(determinant) > 1e-8)
        {
            Vector3d result = new Vector3d(delta.Dot(b.Cross(c)) / determinant, a.Dot(delta.Cross(c)) / determinant, a.Dot(b.Cross(delta)) / determinant);
            if (Within(result, lower, upper)) yield return result;
            yield break;
        }
        Vector3d[] axes = { a, b, c };
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            double cosine = axes[i].Dot(axes[j]), denominator = 1 - cosine * cosine;
            if (denominator < 1e-10) continue;
            double first = (delta.Dot(axes[i]) - cosine * delta.Dot(axes[j])) / denominator;
            double second = (delta.Dot(axes[j]) - cosine * delta.Dot(axes[i])) / denominator;
            if ((delta - axes[i] * first - axes[j] * second).Length > tolerance) continue;
            double[] values = new double[3]; values[i] = first; values[j] = second;
            int k = 3 - i - j;
            double ca = (axes[k].Dot(axes[i]) - cosine * axes[k].Dot(axes[j])) / denominator;
            double cb = (axes[k].Dot(axes[j]) - cosine * axes[k].Dot(axes[i])) / denominator;
            double[] nullAxis = new double[3]; nullAxis[i] = -ca; nullAxis[j] = -cb; nullAxis[k] = 1;
            double low = double.NegativeInfinity, high = double.PositiveInfinity;
            bool feasible = true;
            for (int n = 0; n < 3; n++)
            {
                double minimum = lower[n], maximum = upper[n];
                if (Math.Abs(nullAxis[n]) < 1e-10)
                {
                    if (values[n] < minimum - 1e-9 || values[n] > maximum + 1e-9) feasible = false;
                    continue;
                }
                double bound1 = (minimum - values[n]) / nullAxis[n], bound2 = (maximum - values[n]) / nullAxis[n];
                low = Math.Max(low, Math.Min(bound1, bound2)); high = Math.Min(high, Math.Max(bound1, bound2));
            }
            if (!feasible || low > high) continue;
            double dot = 0, norm = 0;
            for (int n = 0; n < 3; n++) { dot += values[n] * nullAxis[n]; norm += nullAxis[n] * nullAxis[n]; }
            double lambda = Math.Max(low, Math.Min(high, -dot / norm));
            yield return new Vector3d(values[0] + nullAxis[0] * lambda,
                values[1] + nullAxis[1] * lambda, values[2] + nullAxis[2] * lambda);
        }
    }
    private static bool Within(Vector3d value, double[] low, double[] high) => value.IsFinite &&
        value.X >= low[0] - 1e-9 && value.X <= high[0] + 1e-9 && value.Y >= low[1] - 1e-9 && value.Y <= high[1] + 1e-9 &&
        value.Z >= low[2] - 1e-9 && value.Z <= high[2] + 1e-9;

    internal static Vector3d Rotate(Vector3d value, Vector3d unitAxis, double angle)
    {
        return value * Math.Cos(angle) + unitAxis.Cross(value) * Math.Sin(angle) +
            unitAxis * (unitAxis.Dot(value) * (1 - Math.Cos(angle)));
    }
    private static bool Normalize(Vector3d value, out Vector3d unit)
    {
        unit = default;
        if (!value.IsFinite || value.Length < 1e-10 || double.IsInfinity(value.Length)) return false;
        unit = value * (1 / value.Length); return true;
    }
    private static double Wrap(double angle) => Math.Atan2(Math.Sin(angle), Math.Cos(angle));
    private static bool PositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
