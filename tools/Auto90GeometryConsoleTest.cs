using System;
using System.Collections.Generic;
using System.Linq;
using VP_RevitAPI.Internal.Ctn.DirectY;

internal static class Auto90GeometryConsoleTest
{
    private static int assertions;
    private static void Main(string[] args)
    {
        string group = args.Length == 0 ? "all" : args[0];
        if (args.Length > 1 || !new[] { "all", "directions", "guards", "solve" }.Contains(group))
            throw new ArgumentException("Expected all, directions, guards or solve.");
        if (group == "all" || group == "directions") PerpendicularDirections();
        if (group == "all" || group == "guards") ModeAngleGuard();
        if (group == "all" || group == "guards") MeasuredTeeClearance();
        if (group == "all" || group == "solve") RotateFirstGeometry();
        Console.WriteLine($"Auto90 geometry: {assertions} assertions passed ({group}).");
    }

    // Returning the existing 45-degree Y candidates is the production break.
    private static IReadOnlyList<Vector3d> Directions(Vector3d main, Vector3d toward, Vector3d anchor, Vector3d source)
        => DirectYInsertionGeometry.BranchDirections(main, toward, anchor, source, true);
    private static bool Accept90(double dot) => DirectYGuards.SupportsBranchAngle(dot, true);
    private static void PerpendicularDirections()
    {
        foreach (var main in new[] { new Vector3d(1,0,0), new Vector3d(0,1,0), new Vector3d(0,0,1), new Vector3d(3,4,5) })
        {
            var unit = main * (1 / main.Length);
            var values = Directions(main, new Vector3d(4,7,3), new Vector3d(0,0,1), new Vector3d(1,0,0));
            Check(values.Count > 0, "Tee90 requires orientation candidates");
            foreach (var direction in values)
            {
                Near(1, direction.Length, 1e-10, "Candidate is a unit direction");
                Near(0, direction.Dot(unit), 1e-10, "Tee branch is perpendicular in 3D, not 45 degrees or plan-only");
            }
        }
        Check(Directions(default, new Vector3d(0,1,0), new Vector3d(0,0,1), new Vector3d(1,0,0)).Count == 0, "Invalid main axis is rejected");
    }
    private static void ModeAngleGuard()
    {
        Check(Accept90(0), "Auto90 accepts a perpendicular Tee branch");
        Check(Accept90(0.001), "Tiny native angle error is tolerated");
        Check(!Accept90(Math.Sqrt(0.5)), "Auto90 must reject a Y45 returned by Routing Preferences");
        Check(!Accept90(0.2) && !Accept90(double.NaN), "Oblique/invalid Tee angle is rejected");
        Check(!DirectYGuards.SupportsBranchAngle(0), "Default DirectY does not silently change to Tee mode");
        Check(DirectYGuards.SupportsBranchAngle(Math.Sqrt(0.5)), "Existing Y45 behavior stays supported");
    }
    private static void MeasuredTeeClearance()
    {
        // All fixture dimensions are in millimetres (the pure helper is unit-agnostic).
        Check(DirectYInsertionGeometry.TryInitialRunBounds(300, 125, 10, 0.1, true, out double margin),
            "Auto90 must probe a 300 mm run despite being shorter than 4D=500 mm");
        Near(10.1, margin, 1e-10, "Auto90 provisional split keeps minimum segment plus small numeric clearance");
        Check(!DirectYInsertionGeometry.TryInitialRunBounds(300, 125, 10, 0.1, false, out _), "DirectY legacy 4D preflight stays unchanged");
        Check(!DirectYInsertionGeometry.TryInitialRunBounds(20.2, 125, 10, 0.1, true, out _), "A run too short even for two temporary Pipe pieces is rejected");
        Check(DirectYInsertionGeometry.TryMeasuredRunBounds(300, 10, -90, 100, margin, out double low, out double high, out double required),
            "Measured compact Tee fits in a short main");
        Near(100.1, low, 1e-10, "Left takeout plus safe remaining Pipe determines center minimum");
        Near(189.9, high, 1e-10, "Right takeout plus safe remaining Pipe determines center maximum");
        Near(210.2, required, 1e-10, "Required main length is measured Tee span plus safe Pipe on both sides");
        Check(!DirectYInsertionGeometry.TryMeasuredRunBounds(200, 10, -90, 100, margin, out _, out _, out required),
            "Reject actual fitting span that leaves insufficient Pipe despite removing 4D guard");
        Near(210.2, required, 1e-10, "Failure still exposes the measured required length");
        Check(DirectYInsertionGeometry.TryMeasuredRunBounds(250, 10, -180, 20, margin, out low, out high, out _), "Asymmetric takeouts allow off-midpoint fitting placement");
        Near(190.1, low, 1e-10, "Asymmetric Tee keeps its larger left reserve");
        Near(219.9, high, 1e-10, "Asymmetric Tee keeps its smaller right reserve");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1.0, 0.0 })
            Check(!DirectYInsertionGeometry.TryInitialRunBounds(bad, 125, 10, 0.1, true, out _), "Invalid run length never enables a native split");
        Check(!DirectYInsertionGeometry.TryMeasuredRunBounds(300, 10, 100, -90, margin, out _, out _, out _), "Reversed takeout range rejected");
        Check(!DirectYInsertionGeometry.TryMeasuredRunBounds(300, 10, double.NaN, 90, margin, out _, out _, out _), "Invalid measured takeout rejected");
        Check(!DirectYInsertionGeometry.TryInitialRunBounds(300, 0, 10, 0.1, true, out _), "Invalid diameter rejected even though Auto90 does not use 4D");
        var probes = new List<double> { 50, 25, 75, 10.1, 89.9 };
        Check(DirectYInsertionGeometry.TryMeasuredRunBounds(100, 10, -39.92, 39.87, 10.1, out low, out high, out _), "Very narrow but valid measured insertion window exists");
        DirectYInsertionGeometry.AppendMeasuredRunProbes(probes, low, high);
        Check(probes.Skip(5).Any(point => point >= 50.02 - 1e-10 && point <= 50.03 + 1e-10),
            "An invalid canned 50mm probe must not suppress all valid probes merely because the measured window is within 0.1mm");
        int count = probes.Count;
        DirectYInsertionGeometry.AppendMeasuredRunProbes(probes, low, high);
        Check(probes.Count == count, "Repeated measured window does not create infinite duplicate probe attempts");
        DirectYInsertionGeometry.AppendMeasuredRunProbes(probes, 5, 4);
        Check(probes.Count == count, "Invalid measured window does not enqueue probes");
        Check(DirectYInsertionGeometry.TryMeasuredRunBounds(250, 10, -20, 180, 10.1, out low, out high, out required), "Reversing main axis still permits the same asymmetric Tee");
        Near(30.1, low, 1e-10, "Reversed-axis lower bound mirrors the previous upper bound");
        Near(59.9, high, 1e-10, "Reversed-axis upper bound mirrors the previous lower bound");
        Check(DirectYInsertionGeometry.TryMeasuredRunBounds(210.2 / 304.8, 10 / 304.8, -90 / 304.8, 100 / 304.8, 10.1 / 304.8,
            out low, out high, out _), "Exactly fitting zero-width interval survives numeric conversion to Revit feet");
        Check(!DirectYInsertionGeometry.TryMeasuredRunBounds(210.199 / 304.8, 10 / 304.8, -90 / 304.8, 100 / 304.8, 10.1 / 304.8,
            out _, out _, out _), "Numerical equality must not accept a genuinely undersized main");
        Check(!DirectYInsertionGeometry.TryMeasuredRunBounds(double.MaxValue, double.MaxValue, -double.MaxValue,
            double.MaxValue, double.MaxValue, out low, out high, out required) && low == 0 && high == 0 && required == 0,
            "Overflow cannot expose a nonfinite native insertion window");
        var limited = new List<double>();
        for (int i = 0; i < 100; i++) DirectYInsertionGeometry.AppendMeasuredRunProbes(limited, 5 + i, 5.5 + i);
        Check(limited.Count == 20, "Measured retries have a bounded attempt budget instead of growing forever");
    }

    private static void RotateFirstGeometry()
    {
        var input = new DirectYGeometryInput(default, new Vector3d(0,0,1), new Vector3d(3,0,0),
            new Vector3d(1,0,0), new Vector3d(0,7,0), new Vector3d(0,-1,0), 1e-6, 1e-6);
        Check(DirectYGeometry.TrySolve(input, out var solution, out string error), error);
        Near(Math.PI / 2, solution.RotationRadians, 1e-10, "Angle is solved, not supplied by the user");
        Near(0, solution.AxialSlide, 1e-10, "Rotate-only orientation succeeds without height adjustment");
        Near(4, solution.SourceExtension, 1e-10, "Only the branch length is extended to the Tee port");
        Near(0, solution.RotatedSourceDirection.Dot(new Vector3d(1,0,0)), 1e-10, "Final branch direction is 90 degrees to the run");
        var steps = DirectYInsertionGeometry.ElevationSteps(0.5 / 304.8, 100 / 304.8);
        Near(0, steps[0], 1e-12, "All rotations at current height are tried before height changes");
        Near(0.5, steps[1] * 304.8, 1e-10, "First height adjustment is half a millimetre");
        Near(-0.5, steps[2] * 304.8, 1e-10, "Both signs are considered before larger height adjustment");
        for (int i = 1; i < steps.Count; i++) Check(Math.Abs(steps[i]) + 1e-12 >= Math.Abs(steps[i-1]), "Height search is ordered by smallest absolute adjustment");
        var articulated = new DirectYArticulationInput
        {
            UpstreamPivot = default, UpstreamAxis = new Vector3d(0,0,1),
            AnchorPivot = new Vector3d(2,0,0), AnchorAxis = new Vector3d(1,0,0),
            SourceOpen = new Vector3d(2,3,0), SourceDirection = new Vector3d(0,1,0),
            TargetPoint = new Vector3d(5,7,0), TargetOpenDirection = new Vector3d(0,-1,0),
            UpstreamLength = 10, AnchorLength = 10, SourceLength = 3, MinimumLength = 1,
            MaximumAdjustment = 50, PositionTolerance = 1e-8,
            TargetSlideAxis = new Vector3d(1,0,0), TargetSlideMinimum = -5, TargetSlideMaximum = 5
        };
        var options = DirectYArticulation.HeightSolutions(articulated, 0).ToArray();
        Check(options.Length > 0, "Two-joint branch can reach a Tee90 without height adjustment");
        foreach (var result in options)
        {
            Near(0, result.UpstreamSlide, 1e-10, "Zero-height pass does not silently translate the upstream vertical anchor");
            Near(0, result.FinalSourceDirection.Dot(new Vector3d(1,0,0)), 1e-10, "Articulated branch stays perpendicular to the run");
        }
        double angle = 103.37 * Math.PI / 180;
        var targetDirection = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        input = new DirectYGeometryInput(default, new Vector3d(0,0,1), new Vector3d(3,0,0), new Vector3d(1,0,0),
            targetDirection * 7, -targetDirection, 1e-6, 1e-6);
        Check(DirectYGeometry.TrySolve(input, out solution, out error), error);
        Near(103.37, solution.RotationRadians * 180 / Math.PI, 1e-8, "Automatic angle is continuous, not rounded to a half-degree grid");
        ParallelAnchorRotateOnly();
    }
    private static void ParallelAnchorRotateOnly()
    {
        var main = new Vector3d(1,0,0);
        var pivot = new Vector3d(0,10,10);
        var sourceOpen = new Vector3d(0,13,10);
        var sourceDirection = new Vector3d(0,1,0);
        var directions = Directions(main, sourceOpen, main, sourceDirection).ToList();
        var reference = directions[0];
        var rolls = DirectYInsertionGeometry.SinglePositionRolls(pivot, main, sourceOpen, sourceDirection,
            main, default, reference, reference, -50, 50, 10, 3, 0.1, 50, 1e-8);
        directions.AddRange(rolls.Select(roll => DirectYArticulation.Rotate(reference, main, roll)));
        Check(directions.Any(direction => DirectYInsertionGeometry.TryFitSingle(pivot, main, sourceOpen, sourceDirection,
            direction, direction, main, -50, 50, 10, 3, 0.1, 50, 1e-8, out _)),
            "Parallel anchor must find the non-canned roll at zero height before changing elevation");
        foreach (var run in new[] { main, new Vector3d(3,4,5) })
        {
            var u = run * (1 / run.Length);
            var v = u.Cross(new Vector3d(0,0,1)); v = v * (1 / v.Length);
            var w = u.Cross(v);
            Vector3d Map(Vector3d value) => u * value.X + v * value.Y + w * value.Z;
            foreach (double sign in new[] { 1.0, -1.0 })
            {
                var a = u * sign;
                var p = Map(pivot); var q = Map(new Vector3d(0,13,11)); var d = Map(sourceDirection);
                var r = Directions(u, q, a, d)[0];
                var offset = r + u.Cross(r) * 0.3 + u * 0.7;
                var result = DirectYInsertionGeometry.SinglePositionRolls(p, a, q, d, u, default, r, offset,
                    -50, 50, 10, 3, 0.1, 50, 1e-8, 0);
                Check(result.Count > 0, "Parallel/antiparallel inclined runs with eccentric takeouts have rotate-only solutions");
                foreach (double roll in result)
                {
                    var targetOpen = DirectYArticulation.Rotate(r, u, roll);
                    var target = DirectYArticulation.Rotate(offset, u, roll);
                    Check(DirectYInsertionGeometry.TryFitSingle(p, a, q, d, target, targetOpen, u,
                        -50, 50, 10, 3, 0.1, 50, 1e-8, out _, 0), "Every analytic roll satisfies actual length/position bounds");
                    Near(0, targetOpen.Dot(u), 1e-10, "Analytic roll stays perpendicular in 3D");
                }
            }
        }
        Check(DirectYInsertionGeometry.SinglePositionRolls(pivot, main, sourceOpen, sourceDirection,
            main, default, reference, reference, 1, -1, 10, 3, 0.1, 50, 1e-8).Count == 0,
            "Analytic rolls never bypass invalid insertion bounds");
    }
    private static void Near(double expected, double actual, double tolerance, string message) => Check(Math.Abs(expected-actual) <= tolerance, message + $"; expected={expected}, actual={actual}");
    private static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
}
