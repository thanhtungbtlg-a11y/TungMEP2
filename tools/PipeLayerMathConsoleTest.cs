using System;
using VP_RevitAPI.Internal.Ctn;

internal static class PipeLayerMathConsoleTest
{
    private static int assertions;

    private static void Main()
    {
        // Catches the former null intersection at 90 degrees to the reference Pipe.
        var reference = Segment(0, 0, 1000, 1000, 0, 1000, 200);
        var perpendicular = Segment(500, -500, 1400, 500, 500, 1400, 100);
        var click = new PipeLayerPoint(500, 0, 99999);
        Alignment(reference, perpendicular, click, PipeLayerMode.Bop, -450, 0.5, 0.5);
        Alignment(reference, perpendicular, click, PipeLayerMode.Cop, -400, 0.5, 0.5);
        Alignment(reference, perpendicular, click, PipeLayerMode.Top, -350, 0.5, 0.5);

        // The Z of a surface click must not affect the plan station.
        Alignment(reference, perpendicular, new PipeLayerPoint(500, 0, -99999), PipeLayerMode.Cop, -400, 0.5, 0.5);

        // Catches extrapolation: use the nearest real endpoint, not the infinite line.
        var shortSloped = Segment(700, 200, 1300, 900, 200, 1320, 100);
        Alignment(reference, shortSloped, click, PipeLayerMode.Bop, -350, 0.5, 0);
        Alignment(reference, shortSloped, new PipeLayerPoint(1500, 0, 0), PipeLayerMode.Top, -270, 1, 1);

        // Different slopes are sampled locally and retain their shape after Z translation.
        var slopedReference = Segment(0, 0, 1000, 1000, 0, 1020, 200);
        var diagonal = Segment(0, -500, 1300, 1000, 500, 1400, 100);
        Alignment(slopedReference, diagonal, click, PipeLayerMode.Bop, -390, 0.5, 0.5);
        Alignment(Reverse(slopedReference), Reverse(diagonal), click, PipeLayerMode.Bop, -390, 0.5, 0.5);

        // Every plan direction, including anti-parallel and almost perpendicular, must work.
        for (int angle = 0; angle < 360; angle++)
        {
            double radians = angle * Math.PI / 180.0;
            double dx = 500 * Math.Cos(radians);
            double dy = 500 * Math.Sin(radians);
            var candidate = Segment(500 - dx, -dy, 1300, 500 + dx, dy, 1400, 100);
            Alignment(slopedReference, candidate, click, PipeLayerMode.Cop, -340, 0.5, 0.5);
        }

        // Reject ambiguous vertical stations and invalid numeric inputs explicitly.
        Reject(reference, Segment(0, 0, 1000, 0, 0, 1500, 100), click, PipeLayerMode.Bop);
        Reject(Segment(0, 0, 0, 0, 0, 1000, 100), perpendicular, click, PipeLayerMode.Cop);
        Reject(reference, Segment(0, 0, 0, 0, 0, 0, 100), click, PipeLayerMode.Cop);
        Reject(reference, Segment(0, 0, double.NaN, 100, 0, 0, 100), click, PipeLayerMode.Cop);
        Reject(reference, perpendicular, new PipeLayerPoint(double.PositiveInfinity, 0, 0), PipeLayerMode.Cop);
        Reject(reference, Segment(0, 0, 0, 100, 0, 0, -1), click, PipeLayerMode.Bop);
        Reject(reference, perpendicular, click, (PipeLayerMode)99);

        // Catches slope/direction changes or an anchor accidentally moved by Revit.
        var start = new PipeLayerPoint(1, 2, 3);
        var end = new PipeLayerPoint(11, 7, 4);
        Check(PipeLayerMath.EndpointsMatchTranslation(start, end, new PipeLayerPoint(1, 2, 13), new PipeLayerPoint(11, 7, 14), 10, 0.01), "Rigid Z move accepted");
        Check(PipeLayerMath.EndpointsMatchTranslation(start, end, new PipeLayerPoint(11, 7, 14), new PipeLayerPoint(1, 2, 13), 10, 0.01), "Reversed endpoint enumeration accepted");
        Check(!PipeLayerMath.EndpointsMatchTranslation(start, end, new PipeLayerPoint(1, 2, 13), new PipeLayerPoint(11, 7, 15), 10, 0.01), "Slope change rejected");
        Check(!PipeLayerMath.EndpointsMatchTranslation(start, end, new PipeLayerPoint(2, 2, 13), new PipeLayerPoint(11, 7, 14), 10, 0.01), "XY move rejected");
        Check(!PipeLayerMath.EndpointsMatchTranslation(start, end, new PipeLayerPoint(1, 2, 4), new PipeLayerPoint(11, 7, 5), 0, 0.01), "Anchor move rejected");
        Check(!PipeLayerMath.EndpointsMatchTranslation(start, end, start, end, double.NaN, 0.01), "Invalid delta rejected");

        // Axial resizing retains the original infinite 3D line at a fixed Z shift.
        var resizeOriginal = Segment(0,0,0,10,0,1,0.2);
        var resizeActual = Segment(-2,0,4.8,12,0,6.2,0.2);
        Check(PipeLayerMath.TryReadAxialResizeShift(resizeOriginal, resizeActual, 1e-8, out double resizeShift, out string resizeError), resizeError);
        Near(resizeShift, 5, "Sloped resize shift uses unchanged XY stations");
        Check(PipeLayerMath.AxialResizeMatchesTarget(resizeOriginal, resizeActual, 5, 0.5, 1e-8, out resizeError), resizeError);
        Check(PipeLayerMath.AxialResizeMatchesTarget(resizeOriginal, Reverse(resizeActual), 5, 0.5, 1e-8, out resizeError), "Reversed endpoint enumeration during resize is accepted");
        Check(!PipeLayerMath.AxialResizeMatchesTarget(resizeOriginal, resizeActual, 4, 0.5, 1e-8, out resizeError) && resizeError.Contains("cao độ"), "Correct axis with wrong elevation is rejected specifically");
        Check(!PipeLayerMath.AxialResizeMatchesTarget(resizeOriginal, Segment(6,0,5.6,12,0,6.2,0.2), 5, 0.5, 1e-8, out resizeError) && resizeError.Contains("vị trí đo"), "Lost measurement station cannot be replaced by extrapolation");
        Check(!PipeLayerMath.TryReadAxialResizeShift(resizeOriginal, Segment(-2,0.1,4.8,12,0.1,6.2,0.2), 1e-8, out _, out resizeError) && resizeError.Contains("Lệch ngang"), "Parallel sideways displacement is not axial resizing");
        Check(!PipeLayerMath.TryReadAxialResizeShift(resizeOriginal, Segment(-2,0,4.8,12,0,6.3,0.2), 1e-8, out _, out resizeError), "Changed slope is not axial resizing");
        Check(!PipeLayerMath.AxialResizeMatchesTarget(resizeOriginal, resizeActual, 5, 1.1, 1e-8, out _), "Invalid measurement parameter rejected");
        Check(!PipeLayerMath.TryReadAxialResizeShift(resizeOriginal, resizeActual, double.NaN, out _, out _), "Invalid tolerance rejected");
        Check(!PipeLayerMath.TryReadAxialResizeShift(Segment(0,0,0,0,0,1,0.2), resizeActual, 1e-8, out _, out _), "Vertical original axis rejected");
        Check(!PipeLayerMath.TryReadAxialResizeShift(resizeOriginal, Segment(1,0,1,1,0,1,0.2), 1e-8, out _, out _), "Collapsed actual segment rejected");
        Check(!PipeLayerMath.TryReadAxialResizeShift(Segment(0,0,0,10,0,0,0.2),
            Segment(-2,0.009,4.9955,12,0.009,5.0045,0.2), 0.01, out _, out _),
            "Combined lateral/vertical endpoint residual must remain inside one Euclidean tolerance");

        // Catches worsened connector gaps without falsely blaming unchanged old model defects.
        Check(PipeLayerMath.BindingGeometryPreserved(0, 0.01, -1, -1, 0.05), "Connected pair within tolerance accepted");
        Check(!PipeLayerMath.BindingGeometryPreserved(0, 1, -1, -1, 0.05), "New connector gap rejected");
        Check(PipeLayerMath.BindingGeometryPreserved(0.5, 0.5, -1, -1, 0.05), "Unchanged pre-existing gap accepted");
        Check(!PipeLayerMath.BindingGeometryPreserved(0.5, 0.6, -1, -1, 0.05), "Worsened pre-existing gap rejected");
        Check(!PipeLayerMath.BindingGeometryPreserved(0, 0, -1, -0.5, 0.05), "New axis misalignment rejected");
        Check(!PipeLayerMath.BindingGeometryPreserved(0, 0, -1, -Math.Cos(1 * Math.PI / 180.0), 0.05), "Even small new axis deviation rejected");
        double oldDot = -Math.Cos(10 * Math.PI / 180.0);
        Check(PipeLayerMath.BindingGeometryPreserved(0, 0, oldDot, oldDot, 0.05), "Unchanged pre-existing axes accepted");
        Check(!PipeLayerMath.BindingGeometryPreserved(0, 0, oldDot, -Math.Cos(11 * Math.PI / 180.0), 0.05), "Worsened axes rejected");
        Check(!PipeLayerMath.BindingGeometryPreserved(0, double.NaN, -1, -1, 0.05), "Invalid geometry rejected");
        Check(PipeLayerMath.BindingOffsetPreserved(new PipeLayerPoint(0.5, 0, 0), new PipeLayerPoint(0.5, 0, 0), -1, -1, 0.05), "Old offset vector retained");
        Check(!PipeLayerMath.BindingOffsetPreserved(new PipeLayerPoint(0.5, 0, 0), new PipeLayerPoint(0, 0.5, 0), -1, -1, 0.05), "Same-length changed offset vector rejected");

        Console.WriteLine($"PipeLayerMath: {assertions} assertions passed.");
    }

    private static PipeLayerSegment Segment(double x0, double y0, double z0, double x1, double y1, double z1, double diameter)
        => new PipeLayerSegment(new PipeLayerPoint(x0, y0, z0), new PipeLayerPoint(x1, y1, z1), diameter);

    private static PipeLayerSegment Reverse(PipeLayerSegment segment)
        => new PipeLayerSegment(segment.End, segment.Start, segment.OutsideDiameter);

    private static void Alignment(PipeLayerSegment reference, PipeLayerSegment candidate, PipeLayerPoint click,
        PipeLayerMode mode, double expectedDelta, double referenceParameter, double candidateParameter)
    {
        Check(PipeLayerMath.TryCompute(reference, candidate, click, mode, out PipeLayerAlignment result, out string error), "Alignment available: " + error);
        Near(result.DeltaZ, expectedDelta, "Height delta");
        Near(result.ReferenceParameter, referenceParameter, "Reference station clamped");
        Near(result.CandidateParameter, candidateParameter, "Candidate station clamped");
    }

    private static void Reject(PipeLayerSegment reference, PipeLayerSegment candidate, PipeLayerPoint click, PipeLayerMode mode)
    {
        Check(!PipeLayerMath.TryCompute(reference, candidate, click, mode, out _, out string error), "Invalid geometry rejected");
        Check(!string.IsNullOrWhiteSpace(error), "Explicit rejection reason");
    }

    private static void Near(double actual, double expected, string name)
        => Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 1E-7, $"{name}: expected {expected}, got {actual}");

    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(name);
    }
}
