using System;

namespace VP_RevitAPI.Internal.Ctn;

internal enum PipeLayerMode { Bop, Cop, Top }

internal readonly struct PipeLayerPoint
{
    internal PipeLayerPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
    internal double X { get; }
    internal double Y { get; }
    internal double Z { get; }
}

internal readonly struct PipeLayerSegment
{
    internal PipeLayerSegment(PipeLayerPoint start, PipeLayerPoint end, double outsideDiameter)
    { Start = start; End = end; OutsideDiameter = outsideDiameter; }
    internal PipeLayerPoint Start { get; }
    internal PipeLayerPoint End { get; }
    internal double OutsideDiameter { get; }
}

internal readonly struct PipeLayerAlignment
{
    internal PipeLayerAlignment(double deltaZ, double referenceParameter, double candidateParameter)
    { DeltaZ = deltaZ; ReferenceParameter = referenceParameter; CandidateParameter = candidateParameter; }
    internal double DeltaZ { get; }
    internal double ReferenceParameter { get; }
    internal double CandidateParameter { get; }
}

internal static class PipeLayerMath
{
    internal static bool TryCompute(PipeLayerSegment reference, PipeLayerSegment candidate, PipeLayerPoint click,
        PipeLayerMode mode, out PipeLayerAlignment alignment, out string error)
    {
        alignment = default;
        error = string.Empty;
        if (mode != PipeLayerMode.Bop && mode != PipeLayerMode.Cop && mode != PipeLayerMode.Top)
        {
            error = "Kiểu căn cao độ không hợp lệ.";
            return false;
        }
        if (!Valid(reference) || !Valid(candidate) || !Finite(click))
        {
            error = "Không đọc được trục thẳng hoặc đường kính ngoài hợp lệ của Pipe.";
            return false;
        }
        if (!TryStation(reference, click, out double referenceParameter, out PipeLayerPoint referencePoint) ||
            !TryStation(candidate, referencePoint, out double candidateParameter, out PipeLayerPoint candidatePoint))
        {
            error = "Pipe đứng hoặc quá ngắn theo mặt bằng không có vị trí căn cao độ duy nhất; hãy chọn ống ngang/ống dốc.";
            return false;
        }

        double sign = mode == PipeLayerMode.Bop ? -1 : mode == PipeLayerMode.Top ? 1 : 0;
        double delta = referencePoint.Z - candidatePoint.Z +
                       sign * (reference.OutsideDiameter - candidate.OutsideDiameter) / 2.0;
        if (!Finite(delta))
        {
            error = "Giá trị dịch cao độ vượt phạm vi tính toán.";
            return false;
        }
        alignment = new PipeLayerAlignment(delta, referenceParameter, candidateParameter);
        return true;
    }

    internal static bool EndpointsMatchTranslation(PipeLayerPoint originalStart, PipeLayerPoint originalEnd,
        PipeLayerPoint actualStart, PipeLayerPoint actualEnd, double deltaZ, double tolerance)
    {
        if (!Finite(originalStart) || !Finite(originalEnd) || !Finite(actualStart) || !Finite(actualEnd) ||
            !Finite(deltaZ) || !Finite(tolerance) || tolerance < 0)
            return false;
        var expectedStart = new PipeLayerPoint(originalStart.X, originalStart.Y, originalStart.Z + deltaZ);
        var expectedEnd = new PipeLayerPoint(originalEnd.X, originalEnd.Y, originalEnd.Z + deltaZ);
        return (Near(expectedStart, actualStart, tolerance) && Near(expectedEnd, actualEnd, tolerance)) ||
               (Near(expectedStart, actualEnd, tolerance) && Near(expectedEnd, actualStart, tolerance));
    }

    // Derive the Z offset from the original infinite axis at the same XY,
    // not from endpoint averages: resizing a sloped Pipe changes endpoint Z.
    internal static bool TryReadAxialResizeShift(PipeLayerSegment original, PipeLayerSegment actual,
        double tolerance, out double deltaZ, out string error)
    {
        deltaZ = 0; error = string.Empty;
        if (!Valid(original) || !Valid(actual) || !Finite(tolerance) || tolerance < 0)
        { error = "Dữ liệu trục/co giãn không hợp lệ."; return false; }
        double dx = original.End.X - original.Start.X, dy = original.End.Y - original.Start.Y;
        double lengthSquared = dx * dx + dy * dy;
        double actualDx = actual.End.X - actual.Start.X, actualDy = actual.End.Y - actual.Start.Y;
        if (!Finite(lengthSquared) || lengthSquared <= 1e-18 ||
            !Finite(actualDx * actualDx + actualDy * actualDy) || actualDx * actualDx + actualDy * actualDy <= 1e-18)
        { error = "Pipe đứng hoặc quá ngắn theo mặt bằng."; return false; }
        double first = Offset(actual.Start, out double firstSide), second = Offset(actual.End, out double secondSide);
        double side = Math.Max(firstSide, secondSide);
        if (!Finite(first) || !Finite(second) || !Finite(side))
        { error = "Sai lệch trục vượt phạm vi tính toán."; return false; }
        if (side > tolerance)
        { error = $"Lệch ngang khỏi trục gốc {side * 304.8:F3} mm; chỉ cho phép co/giãn dọc trục."; return false; }
        deltaZ = first + (second - first) / 2;
        if (!Finite(deltaZ) || Math.Abs(first - second) > tolerance)
        { error = $"Hướng/độ dốc bị đổi; sai khác Z giữa hai đầu sau quy về trục gốc {Math.Abs(first - second) * 304.8:F3} mm."; return false; }
        double firstResidual = Math.Sqrt(firstSide * firstSide + (first - deltaZ) * (first - deltaZ));
        double secondResidual = Math.Sqrt(secondSide * secondSide + (second - deltaZ) * (second - deltaZ));
        double residual = Math.Max(firstResidual, secondResidual);
        if (!Finite(residual) || residual > tolerance)
        { error = $"Sai lệch tổng hợp khỏi trục 3D {residual * 304.8:F3} mm; không phải co/giãn dọc trục trong sai số cho phép."; return false; }
        return true;

        double Offset(PipeLayerPoint point, out double sideDistance)
        {
            double t = ((point.X - original.Start.X) * dx + (point.Y - original.Start.Y) * dy) / lengthSquared;
            double x = original.Start.X + t * dx, y = original.Start.Y + t * dy;
            sideDistance = Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
            return point.Z - (original.Start.Z + t * (original.End.Z - original.Start.Z));
        }
    }

    internal static bool AxialResizeMatchesTarget(PipeLayerSegment original, PipeLayerSegment actual,
        double expectedShift, double measurementParameter, double tolerance, out string error)
    {
        error = string.Empty;
        if (!Finite(expectedShift) || !Finite(measurementParameter) || measurementParameter < 0 || measurementParameter > 1)
        { error = "Thiếu vị trí đo/cao độ đích hợp lệ."; return false; }
        if (!TryReadAxialResizeShift(original, actual, tolerance, out double actualShift, out error)) return false;
        if (Math.Abs(actualShift - expectedShift) > tolerance)
        { error = $"Lệch cao độ đích {(actualShift - expectedShift) * 304.8:F3} mm (cần dịch {expectedShift * 304.8:F3} mm, thực tế {actualShift * 304.8:F3} mm)."; return false; }
        var station = new PipeLayerPoint(original.Start.X + measurementParameter * (original.End.X - original.Start.X),
            original.Start.Y + measurementParameter * (original.End.Y - original.Start.Y),
            original.Start.Z + measurementParameter * (original.End.Z - original.Start.Z) + expectedShift);
        if (!Finite(station) || !TryStation(actual, station, out _, out PipeLayerPoint actualStation) || !Near(station, actualStation, tolerance))
        { error = "Pipe bị co ngắn/trượt vượt vị trí đo; không còn đoạn ống thật tại điểm căn BOP/COP/TOP."; return false; }
        return true;
    }

    internal static bool BindingGeometryPreserved(double originalGap, double actualGap,
        double originalAxisDot, double actualAxisDot, double tolerance)
    {
        if (!Finite(originalGap) || !Finite(actualGap) || !Finite(tolerance) ||
            originalGap < 0 || actualGap < 0 || tolerance < 0 ||
            !ValidDot(originalAxisDot) || !ValidDot(actualAxisDot))
            return false;
        double oldDeviation = Math.Acos(Clamp(-originalAxisDot, -1, 1)) * 180.0 / Math.PI;
        double newDeviation = Math.Acos(Clamp(-actualAxisDot, -1, 1)) * 180.0 / Math.PI;
        // Unchanged pre-existing imperfections are not introduced by PipeLayer.
        return actualGap <= Math.Max(tolerance, originalGap + tolerance) &&
               newDeviation <= oldDeviation + 0.1;
    }

    private static bool TryStation(PipeLayerSegment segment, PipeLayerPoint reference,
        out double parameter, out PipeLayerPoint point)
    {
        parameter = 0;
        point = default;
        double dx = segment.End.X - segment.Start.X;
        double dy = segment.End.Y - segment.Start.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (!Finite(lengthSquared) || lengthSquared <= 1E-18) return false;
        double projected = ((reference.X - segment.Start.X) * dx + (reference.Y - segment.Start.Y) * dy) / lengthSquared;
        if (!Finite(projected)) return false;
        parameter = Clamp(projected, 0, 1);
        point = new PipeLayerPoint(segment.Start.X + parameter * dx, segment.Start.Y + parameter * dy,
            segment.Start.Z + parameter * (segment.End.Z - segment.Start.Z));
        return Finite(point);
    }

    internal static bool BindingOffsetPreserved(PipeLayerPoint originalOffset, PipeLayerPoint actualOffset,
        double originalAxisDot, double actualAxisDot, double tolerance)
    {
        double originalGap = Math.Sqrt(originalOffset.X * originalOffset.X + originalOffset.Y * originalOffset.Y + originalOffset.Z * originalOffset.Z);
        double actualGap = Math.Sqrt(actualOffset.X * actualOffset.X + actualOffset.Y * actualOffset.Y + actualOffset.Z * actualOffset.Z);
        return Near(originalOffset, actualOffset, tolerance) &&
               BindingGeometryPreserved(originalGap, actualGap, originalAxisDot, actualAxisDot, tolerance);
    }

    private static bool Valid(PipeLayerSegment segment) => Finite(segment.Start) && Finite(segment.End) &&
        Finite(segment.OutsideDiameter) && segment.OutsideDiameter > 0;
    private static bool Finite(PipeLayerPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static bool ValidDot(double v) => Finite(v) && v >= -1.000000001 && v <= 1.000000001;
    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    private static bool Near(PipeLayerPoint first, PipeLayerPoint second, double tolerance)
    {
        if (!Finite(first) || !Finite(second)) return false;
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        double dz = first.Z - second.Z;
        double squared = dx * dx + dy * dy + dz * dz;
        return Finite(squared) && squared <= tolerance * tolerance;
    }
}
