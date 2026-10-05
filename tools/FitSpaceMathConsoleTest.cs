using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceMathConsoleTest
{
	private static int _assertions;

	private static void Main()
	{
		CalculatesE90To45CompatibleSpacing();
		AcceptsSmallConnectorAxisDeviation();
		RejectsLargeConnectorAxisDeviation();
		GeneralizesSymmetricIncludedAngle();
		RejectsOutsideAngleRange();
		RejectsInvalidDiameter();
		DetectsAlreadyNormalized();
		ClassifiesDuplicateOwnership();
		FindsPartialOwnershipOverlap();
		Console.WriteLine($"FitSpace math tests passed: {_assertions} assertions.");
	}

	private static void CalculatesE90To45CompatibleSpacing()
	{
		bool ok = FitSpaceMath.TryCalculate(
			new FitSpaceScalarInput(125.0, Degrees(90.0)),
			out FitSpaceScalarTarget target,
			out string error);
		AssertTrue(ok, error);
		AssertNear(275.0, target.RawMiddleLength, 1E-09, "DN100 ElbowData raw middle length");
		AssertNear(275.0 / Math.Sqrt(2.0), target.Standoff, 1E-09, "90-degree standoff");
	}

	private static void AcceptsSmallConnectorAxisDeviation()
	{
		AssertConnectorOpposition(-Math.Cos(Degrees(3.0)), 5.0, true, "Three-degree connector deviation");
		AssertConnectorOpposition(-1.0, 5.0, true, "Perfectly opposed connector axes");
	}

	private static void RejectsLargeConnectorAxisDeviation()
	{
		AssertConnectorOpposition(-Math.Cos(Degrees(6.0)), 5.0, false, "Six-degree connector deviation");
		AssertConnectorOpposition(1.0, 5.0, false, "Same-direction connector axes");
	}

	private static void GeneralizesSymmetricIncludedAngle()
	{
		bool ok = FitSpaceMath.TryCalculate(
			new FitSpaceScalarInput(100.0, Degrees(60.0)),
			out FitSpaceScalarTarget target,
			out string error);
		AssertTrue(ok, error);
		AssertNear(220.0, target.RawMiddleLength, 1E-09, "60-degree raw length");
		AssertNear(220.0, target.Standoff, 1E-09, "60-degree standoff");
	}

	private static void RejectsOutsideAngleRange()
	{
		AssertFalse(Try(100.0, 14.99), "14.99 degrees must be rejected");
		AssertFalse(Try(100.0, 165.01), "165.01 degrees must be rejected");
		AssertTrue(Try(100.0, 15.0), "15 degrees must be accepted");
		AssertTrue(Try(100.0, 165.0), "165 degrees must be accepted");
	}

	private static void RejectsInvalidDiameter()
	{
		AssertFalse(Try(0.0, 90.0), "Zero size must be rejected");
		AssertFalse(Try(-1.0, 90.0), "Negative size must be rejected");
		AssertFalse(Try(double.NaN, 90.0), "NaN size must be rejected");
		AssertFalse(Try(double.PositiveInfinity, 90.0), "Infinite size must be rejected");
	}

	private static void DetectsAlreadyNormalized()
	{
		AssertFalse(FitSpaceMath.IsNormalized(100.0, 100.0, -0.1), "Negative tolerance must fail");
		AssertTrue(FitSpaceMath.IsNormalized(100.4, 100.0, 0.5), "0.4 delta must be normalized");
		AssertFalse(FitSpaceMath.IsNormalized(100.6, 100.0, 0.5), "0.6 delta must not be normalized");
		AssertFalse(FitSpaceMath.IsNormalized(double.NaN, 100.0, 0.5), "NaN current value must fail");
	}

	private static void ClassifiesDuplicateOwnership()
	{
		List<FitSpaceOwnershipSet> sets = new List<FitSpaceOwnershipSet>
		{
			new FitSpaceOwnershipSet(10, new long[] { 1, 2, 3, 4, 5 }),
			new FitSpaceOwnershipSet(20, new long[] { 5, 4, 3, 2, 1 }),
			new FitSpaceOwnershipSet(30, new long[] { 6, 7, 8, 9, 10 })
		};
		FitSpaceOwnershipPolicy.Classify(sets, out IReadOnlyList<long> duplicates, out IReadOnlyList<FitSpaceOwnershipOverlap> overlaps);
		AssertSequence(new long[] { 20 }, duplicates, "Only later identical cluster is duplicate");
		AssertEqual(0, overlaps.Count, "Duplicate is not partial overlap");
	}

	private static void FindsPartialOwnershipOverlap()
	{
		List<FitSpaceOwnershipSet> sets = new List<FitSpaceOwnershipSet>
		{
			new FitSpaceOwnershipSet(101, new long[] { 1, 2, 3, 4, 5 }),
			new FitSpaceOwnershipSet(202, new long[] { 5, 6, 7, 8, 9 }),
			new FitSpaceOwnershipSet(303, new long[] { 10, 11, 12, 13, 14 })
		};
		FitSpaceOwnershipPolicy.Classify(sets, out IReadOnlyList<long> duplicates, out IReadOnlyList<FitSpaceOwnershipOverlap> overlaps);
		AssertEqual(0, duplicates.Count, "Partial overlap is not duplicate");
		AssertEqual(1, overlaps.Count, "Exactly one overlap expected");
		AssertEqual(101L, overlaps[0].FirstKey, "Overlap first key");
		AssertEqual(202L, overlaps[0].SecondKey, "Overlap second key");
	}

	private static bool Try(double size, double angleDegrees)
	{
		return FitSpaceMath.TryCalculate(
			new FitSpaceScalarInput(size, Degrees(angleDegrees)),
			out _,
			out _);
	}

	private static double Degrees(double degrees)
	{
		return degrees * Math.PI / 180.0;
	}

	private static void AssertConnectorOpposition(
		double dotProduct,
		double maximumDeviationDegrees,
		bool expected,
		string message)
	{
		MethodInfo method = typeof(FitSpaceMath).GetMethod(
			"IsConnectorAxisOpposed",
			BindingFlags.Static | BindingFlags.NonPublic);
		AssertTrue(method != null, "FitSpaceMath.IsConnectorAxisOpposed must exist");
		bool actual = (bool)method.Invoke(null, new object[] { dotProduct, maximumDeviationDegrees });
		AssertEqual(expected, actual, message);
	}

	private static void AssertTrue(bool condition, string message)
	{
		_assertions++;
		if (!condition) throw new InvalidOperationException("AssertTrue failed: " + message);
	}

	private static void AssertFalse(bool condition, string message)
	{
		AssertTrue(!condition, message);
	}

	private static void AssertNear(double expected, double actual, double tolerance, string message)
	{
		_assertions++;
		if (Math.Abs(expected - actual) > tolerance)
		{
			throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
		}
	}

	private static void AssertEqual<T>(T expected, T actual, string message)
	{
		_assertions++;
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
		}
	}

	private static void AssertSequence(IEnumerable<long> expected, IEnumerable<long> actual, string message)
	{
		_assertions++;
		if (!expected.SequenceEqual(actual))
		{
			throw new InvalidOperationException($"{message}: expected [{string.Join(",", expected)}], actual [{string.Join(",", actual)}]");
		}
	}
}
