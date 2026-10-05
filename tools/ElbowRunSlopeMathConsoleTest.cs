using System;
using VP_RevitAPI.Commands.CTN;

internal static class ElbowRunSlopeMathConsoleTest
{
    private static int _assertions;

    public static int Main()
    {
        try
        {
            AssertRatio("0.5000%", 0.005, "percent with dot");
            AssertRatio("0,5000 %", 0.005, "percent with comma");
            AssertRatio("1:200", 0.005, "colon ratio");
            AssertRatio("1/100", 0.01, "fraction ratio");
            AssertRatio("0.286 degrees", Math.Tan(0.286 * Math.PI / 180.0),
                "degree value");

            AssertNear(
                ElbowRunSlopeMath.ResolveSignedRatio(0.005, PipeSlopeMode.Up, -0.02),
                0.005,
                "Slope Up must create a positive rise");
            AssertNear(
                ElbowRunSlopeMath.ResolveSignedRatio(0.005, PipeSlopeMode.Down, 0.02),
                -0.005,
                "Slope Down must create a negative rise");
            AssertNear(
                ElbowRunSlopeMath.ResolveSignedRatio(0.005, PipeSlopeMode.Off, 0.02),
                0.0,
                "Slope Off must create a horizontal run");
            AssertNear(
                ElbowRunSlopeMath.ResolveSignedRatio(0.005, PipeSlopeMode.Unknown, -0.02),
                -0.02,
                "Unavailable ribbon state must preserve the selected pipe slope");
            AssertNear(
                ElbowRunSlopeMath.CalculateRise(300.0, 0.005),
                1.5,
                "A 0.5 percent slope over 300 mm must rise 1.5 mm");

            Console.WriteLine(
                "Elbow run slope regression passed: " +
                _assertions + " assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Elbow run slope regression failed:");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void AssertRatio(string text, double expected, string description)
    {
        double actual;
        bool parsed = ElbowRunSlopeMath.TryParseRatio(text, out actual);
        AssertTrue(parsed, "Could not parse " + description);
        AssertNear(actual, expected, description);
    }

    private static void AssertNear(double actual, double expected, string description)
    {
        AssertTrue(Math.Abs(actual - expected) <= 1e-8, description);
    }

    private static void AssertTrue(bool condition, string description)
    {
        _assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(description + ".");
        }
    }
}
