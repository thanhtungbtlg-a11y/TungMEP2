using System;
using VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYGeometryConsoleTest
{
	private const double AngularTolerance = 1e-6;
	private const double PositionTolerance = 1e-6;
	private static int _assertions;

	private static void Main()
	{
		SolvesPositiveQuarterTurnWithIndependentSlideAndExtension();
		UsesDisplacedPivotAndAllowsShortening();
		PreservesNegativeRotationSign();
		SeparatesAxialSlideFromSlopedPipeExtension();
		NormalizesInputDirections();
		RejectsTargetDirectionOutsideRotationCone();
		RejectsNonCoplanarTargetPoint();
		AcceptsResidualWithinPositionTolerance();
		RejectsParallelAnchorAndSourceAxes();
		RejectsInvalidInputsAndTolerances();
		ClassifiesNominalFortyFiveDegreeY();
		ClassifiesBranchAtFirstIndex();
		AcceptsShallowOpenBranch();
		RejectsShallowOpenMainPort();
		RejectsAmbiguousMainPair();
		RejectsNearTieWithinAngularGap();
		RejectsInvalidBranchClassificationInputs();
		SolvesElevationAndTwoJointRotation();
		SolvesNonzeroOuterAndInnerRotations();
		PreservesNegativeArticulationAndShortening();
		RejectsImpossibleArticulation();
		RejectsNonfiniteArticulationLengths();
		SharesParallelAxisAdjustmentWithinLimits();
		RotatesFreeOuterJointForPosition();
		RejectsUnsupportedCloserTeeAndIncompleteScan();
		DetectsFittingShapeChangesAfterRigidMotion();
		FindsSingleJointInsertionWithoutHeightChange();
		ConstrainsArticulationHeightWhileFindingInsertion();
		SearchesHeightInSmallRotateFirstSteps();
		RejectsUnboundedElevationEnumeration();
		SearchesHalfMillimetreHeightsAcrossFullRange();
		LocksSingleJointHeightBeforeTryingInsertion();
		PreventsHiddenInnerAnchorElevation();
		AllowsExplicitInnerHeightWithHorizontalUpstream();
		FindsPositionDerivedZeroHeightRoll();
		AccountsForRolledFittingTakeoutAndRejectsOutsideMain();
		RefinesOffGridPositionRolls();
		FindsHalfDegreeRollInNarrowFeasibleDirectionWindow();
		ApprovesOnlyConfirmedTemporaryDeletion();
		RejectsDeletionOfExistingOrRetainedElements();
		ChecksRealDeletionAgainstApprovedProbe();
		ApprovesNewCenterLineWithOneMatchingTemporaryOwnerAndNoRetainedDependency();
		RejectsCenterLineWithoutOneMatchingTemporaryOwnerOrWithRetainedDependency();
		ReportsBindingIdentityAndMeasuredGapInMillimetres();
		ReportsMissingBindingGeometryWithoutInventingMeasurements();
		DistinguishesBarelyFailingBindingGapFromTolerance();
		RunsDiagnosticMeasurementAndContainsOrdinaryGetterFailure();
		RethrowsFatalDiagnosticFailure();
		PreservesOnlyOriginalInternalOffsets();
		TransformsInternalPortsInNativeOuterThenInnerOrder();
		RejectsInternalPortAndOffsetDrift();
		TransformsMeasuredReducerOffsetWithoutChangingIt();
		RejectsUncommandedFittingRollWithUnchangedConnectorAxis();
		Console.WriteLine($"DirectY geometry tests passed: {_assertions} assertions.");
	}

	private static void SolvesPositiveQuarterTurnWithIndependentSlideAndExtension()
	{
		// A wrong angle sign, omitted point rotation, or conflated s/t breaks this case.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0, 7, 3), new Vector3d(0, -1, 0));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(Math.PI / 2, result.RotationRadians, 1e-10, "positive quarter-turn");
		AssertNear(3, result.AxialSlide, 1e-10, "anchor slide");
		AssertNear(5, result.SourceExtension, 1e-10, "source extension");
		AssertVector(new Vector3d(0, 2, 0), result.RotatedSourceOpenPoint, 1e-10, "rotated open point");
		AssertVector(new Vector3d(0, 1, 0), result.RotatedSourceDirection, 1e-10, "rotated source direction");
		AssertNear(0, result.ResidualDistance, 1e-10, "zero residual");
	}

	private static void UsesDisplacedPivotAndAllowsShortening()
	{
		// Rotation about the world origin or clamping a negative extension would fail.
		DirectYGeometryInput input = Input(
			new Vector3d(10, -5, 2), new Vector3d(0, 0, 1),
			new Vector3d(12, -5, 2), new Vector3d(1, 0, 0),
			new Vector3d(10, -5, 3), new Vector3d(0, -1, 0));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(Math.PI / 2, result.RotationRadians, 1e-10, "displaced-pivot angle");
		AssertVector(new Vector3d(10, -3, 2), result.RotatedSourceOpenPoint, 1e-10, "pivot-relative point");
		AssertNear(1, result.AxialSlide, 1e-10, "displaced-pivot slide");
		AssertNear(-2, result.SourceExtension, 1e-10, "signed shortening");
	}

	private static void PreservesNegativeRotationSign()
	{
		// Taking only the absolute angle would rotate to the wrong side.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0, -6, -2), new Vector3d(0, 1, 0));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(-Math.PI / 2, result.RotationRadians, 1e-10, "negative quarter-turn");
		AssertNear(-2, result.AxialSlide, 1e-10, "negative axial slide");
		AssertNear(4, result.SourceExtension, 1e-10, "negative-angle extension");
	}

	private static void SeparatesAxialSlideFromSlopedPipeExtension()
	{
		// A projection-only solver would overcount the slide by the pipe's axial extension.
		double root3 = Math.Sqrt(3);
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2 * root3, 0, 2), new Vector3d(root3 / 2, 0, 0.5),
			new Vector3d(0, 4 * root3, 7), new Vector3d(0, -root3 / 2, -0.5));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(Math.PI / 2, result.RotationRadians, 1e-10, "sloped angle");
		AssertNear(3, result.AxialSlide, 1e-10, "sloped axial slide");
		AssertNear(4, result.SourceExtension, 1e-10, "sloped extension");
	}

	private static void NormalizesInputDirections()
	{
		// Treating direction lengths as displacement scale would corrupt s and t.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 2),
			new Vector3d(2, 0, 0), new Vector3d(3, 0, 0),
			new Vector3d(0, 7, 3), new Vector3d(0, -4, 0));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(3, result.AxialSlide, 1e-10, "normalized anchor slide");
		AssertNear(5, result.SourceExtension, 1e-10, "normalized source extension");
	}

	private static void RejectsTargetDirectionOutsideRotationCone()
	{
		// Rotation about Z cannot change the source direction's Z component.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0, 7, 3), new Vector3d(0, -Math.Sqrt(3) / 2, -0.5));
		AssertFalse(DirectYGeometry.TrySolve(input, out _, out _), "mismatched axial direction must fail");
	}

	private static void RejectsNonCoplanarTargetPoint()
	{
		// Dropping the residual check would falsely accept a skew connection.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0.01, 7, 3), new Vector3d(0, -1, 0));
		AssertFalse(DirectYGeometry.TrySolve(input, out _, out _), "off-plane connector must fail");
	}

	private static void AcceptsResidualWithinPositionTolerance()
	{
		// Rejecting every nonzero numerical residual would be too strict.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0.0000005, 7, 3), new Vector3d(0, -1, 0));
		AssertTrue(DirectYGeometry.TrySolve(input, out DirectYGeometrySolution result, out string error), error);
		AssertNear(0.0000005, result.ResidualDistance, 1e-10, "accepted residual distance");
	}

	private static void RejectsParallelAnchorAndSourceAxes()
	{
		// Parallel basis directions leave slide and extension underdetermined.
		DirectYGeometryInput input = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(0, 0, 2), new Vector3d(0, 0, 1),
			new Vector3d(0, 0, 7), new Vector3d(0, 0, -1));
		AssertFalse(DirectYGeometry.TrySolve(input, out _, out _), "parallel source and anchor must fail");
	}

	private static void RejectsInvalidInputsAndTolerances()
	{
		// Zero/nonfinite vectors or tolerances must not produce an apparent solution.
		DirectYGeometryInput valid = Input(
			new Vector3d(0, 0, 0), new Vector3d(0, 0, 1),
			new Vector3d(2, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(0, 7, 3), new Vector3d(0, -1, 0));
		AssertFalse(DirectYGeometry.TrySolve(Input(valid.RotationPivot, new Vector3d(0, 0, 0), valid.SourceOpenPoint, valid.SourceAxisDirection, valid.TargetConnectorPoint, valid.TargetOpenDirection), out _, out _), "zero anchor axis");
		AssertFalse(DirectYGeometry.TrySolve(Input(valid.RotationPivot, valid.AnchorAxisDirection, valid.SourceOpenPoint, new Vector3d(0, 0, 0), valid.TargetConnectorPoint, valid.TargetOpenDirection), out _, out _), "zero source axis");
		AssertFalse(DirectYGeometry.TrySolve(Input(valid.RotationPivot, valid.AnchorAxisDirection, valid.SourceOpenPoint, valid.SourceAxisDirection, valid.TargetConnectorPoint, new Vector3d(0, 0, 0)), out _, out _), "zero target direction");
		AssertFalse(DirectYGeometry.TrySolve(Input(new Vector3d(double.NaN, 0, 0), valid.AnchorAxisDirection, valid.SourceOpenPoint, valid.SourceAxisDirection, valid.TargetConnectorPoint, valid.TargetOpenDirection), out _, out _), "nonfinite pivot");
		AssertFalse(DirectYGeometry.TrySolve(new DirectYGeometryInput(valid.RotationPivot, valid.AnchorAxisDirection, valid.SourceOpenPoint, valid.SourceAxisDirection, valid.TargetConnectorPoint, valid.TargetOpenDirection, -1, PositionTolerance), out _, out _), "negative angular tolerance");
		AssertFalse(DirectYGeometry.TrySolve(new DirectYGeometryInput(valid.RotationPivot, valid.AnchorAxisDirection, valid.SourceOpenPoint, valid.SourceAxisDirection, valid.TargetConnectorPoint, valid.TargetOpenDirection, AngularTolerance, double.PositiveInfinity), out _, out _), "nonfinite position tolerance");
	}

	private static void ClassifiesNominalFortyFiveDegreeY()
	{
		// Selecting a branch/main pair instead of the straight run breaks this case.
		double diagonal = Math.Sqrt(0.5);
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(diagonal, diagonal, 0), 2, Degrees(2),
			out DirectYBranchClassification result, out string error);
		AssertTrue(ok, error);
		AssertEqual(0, result.MainFirstIndex, "nominal main first");
		AssertEqual(1, result.MainSecondIndex, "nominal main second");
		AssertEqual(2, result.BranchIndex, "nominal branch");
	}

	private static void ClassifiesBranchAtFirstIndex()
	{
		// Hardcoding the last connector as branch breaks a valid connector ordering.
		double diagonal = Math.Sqrt(0.5);
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(diagonal, diagonal, 0), new Vector3d(1, 0, 0),
			new Vector3d(-1, 0, 0), 0, Degrees(2),
			out DirectYBranchClassification result, out string error);
		AssertTrue(ok, error);
		AssertEqual(1, result.MainFirstIndex, "reordered main first");
		AssertEqual(2, result.MainSecondIndex, "reordered main second");
		AssertEqual(0, result.BranchIndex, "reordered branch");
	}

	private static void AcceptsShallowOpenBranch()
	{
		// A fixed dot-gap threshold of 0.02 incorrectly marks a 10-degree branch ambiguous.
		double shallow = Degrees(10);
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(Math.Cos(shallow), Math.Sin(shallow), 0), 2, Degrees(2),
			out DirectYBranchClassification result, out string error);
		AssertTrue(ok, error);
		AssertEqual(2, result.BranchIndex, "shallow branch remains open port");
	}

	private static void RejectsShallowOpenMainPort()
	{
		// The sole open port must not be mistaken for a branch just because another pair is nearly opposed.
		double shallow = Degrees(10);
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(Math.Cos(shallow), Math.Sin(shallow), 0), 1, Degrees(2),
			out _, out _);
		AssertFalse(ok, "shallow-angle open main port must fail");
	}

	private static void RejectsAmbiguousMainPair()
	{
		// An arbitrary tie-break would misclassify a symmetric three-way fitting.
		double diagonal = Math.Sqrt(3) / 2;
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(1, 0, 0), new Vector3d(-0.5, diagonal, 0),
			new Vector3d(-0.5, -diagonal, 0), 2, Degrees(2),
			out _, out _);
		AssertFalse(ok, "three equally opposed pairs are ambiguous");
	}

	private static void RejectsNearTieWithinAngularGap()
	{
		// Ignoring the configured angular gap would accept an almost coincident branch/main pair.
		double shallow = Degrees(1);
		bool ok = DirectYGeometry.TryClassifyOpenBranch(
			new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0),
			new Vector3d(Math.Cos(shallow), Math.Sin(shallow), 0), 2, Degrees(2),
			out _, out _);
		AssertFalse(ok, "one-degree runner-up gap is ambiguous at two-degree tolerance");
	}

	private static void RejectsInvalidBranchClassificationInputs()
	{
		// Bad connector data must not silently classify as a Y branch.
		Vector3d left = new Vector3d(-1, 0, 0);
		Vector3d right = new Vector3d(1, 0, 0);
		Vector3d branch = new Vector3d(0, 1, 0);
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, right, branch, -1, Degrees(2), out _, out _), "negative open index");
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, right, branch, 3, Degrees(2), out _, out _), "out-of-range open index");
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, right, branch, 2, -1, out _, out _), "negative angular gap");
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, right, branch, 2, double.NaN, out _, out _), "nonfinite angular gap");
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, new Vector3d(0, 0, 0), branch, 2, Degrees(2), out _, out _), "zero connector axis");
		AssertFalse(DirectYGeometry.TryClassifyOpenBranch(left, right, new Vector3d(double.PositiveInfinity, 0, 0), 2, Degrees(2), out _, out _), "nonfinite connector axis");
	}

	private static double Degrees(double degrees)
	{
		return degrees * Math.PI / 180;
	}

	private static void SolvesElevationAndTwoJointRotation()
	{
		double h = Math.Sqrt(0.5);
		var input = new DirectYArticulationInput
		{
			UpstreamPivot = new Vector3d(0, 0, 0), UpstreamAxis = new Vector3d(0, 0, 1),
			AnchorPivot = new Vector3d(2, 0, 0), AnchorAxis = new Vector3d(1, 0, 0),
			SourceOpen = new Vector3d(2, 2, 0), SourceDirection = new Vector3d(0, 1, 0),
			TargetPoint = new Vector3d(3, 3 * h, 1 + 3 * h), TargetOpenDirection = new Vector3d(0, -h, -h),
			UpstreamLength = 5, AnchorLength = 2, SourceLength = 2,
			MinimumLength = 0.1, MaximumAdjustment = 10, PositionTolerance = 1e-6
		};
		AssertTrue(DirectYArticulation.TrySolve(input, out var solution, out string error), error);
		AssertNear(1, solution.UpstreamSlide, 1e-6, "elevation slide");
		AssertNear(1, solution.AnchorSlide, 1e-6, "anchor slide");
		AssertNear(1, solution.SourceExtension, 1e-6, "source extension");
		AssertVector(new Vector3d(0, h, h), solution.FinalSourceDirection, 1e-6, "pitched branch axis");
		AssertNear(0, solution.Residual, 1e-6, "articulation residual");
		input.MaximumAdjustment = 0.5;
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "adjustment limit");
	}

	private static void RejectsImpossibleArticulation()
	{
		var input = new DirectYArticulationInput
		{
			UpstreamAxis = new Vector3d(0, 0, 1), AnchorAxis = new Vector3d(0, 0, 1),
			SourceDirection = new Vector3d(1, 0, 0), TargetOpenDirection = new Vector3d(0, 0, -1),
			UpstreamLength = 5, AnchorLength = 2, SourceLength = 2,
			MinimumLength = 0.1, MaximumAdjustment = 10, PositionTolerance = 1e-6
		};
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "two joints still cannot change invariant bend angle");
	}

	private static void SolvesNonzeroOuterAndInnerRotations()
	{
		// Both point rotations and order matter: outer +90° about Z, inner +45° about Y.
		double h = Math.Sqrt(0.5);
		var input = new DirectYArticulationInput
		{
			UpstreamAxis = new Vector3d(0, 0, 1), AnchorAxis = new Vector3d(1, 0, 0),
			AnchorPivot = new Vector3d(2, 0, 0), SourceOpen = new Vector3d(2, 2, 0),
			SourceDirection = new Vector3d(0, 1, 0), TargetPoint = new Vector3d(-3 * h, 3, 1 + 3 * h),
			TargetOpenDirection = new Vector3d(h, 0, -h), UpstreamLength = 5,
			AnchorLength = 2, SourceLength = 2, MinimumLength = 0.1, MaximumAdjustment = 10, PositionTolerance = 1e-6
		};
		AssertTrue(DirectYArticulation.TrySolve(input, out var result, out string error), error);
		AssertNear(Degrees(90), result.OuterRotation, 1e-6, "outer quarter turn");
		AssertNear(Degrees(45), result.InnerRotation, 1e-6, "inner pitch");
		AssertNear(1, result.UpstreamSlide, 1e-6, "upstream slide after turn");
		AssertNear(1, result.AnchorSlide, 1e-6, "anchor slide after turn");
		AssertNear(1, result.SourceExtension, 1e-6, "extension after turn");
		AssertVector(new Vector3d(0, 2, 1), result.TransformedAnchorPivot, 1e-6, "actual inner pivot");
		AssertVector(new Vector3d(0, 1, 0), result.TransformedAnchorAxis, 1e-6, "actual inner axis");
	}

	private static void PreservesNegativeArticulationAndShortening()
	{
		// Offset origin, negative rotations and shortening catch pivot/sign/length regression.
		double h = Math.Sqrt(0.5);
		var input = new DirectYArticulationInput
		{
			UpstreamPivot = new Vector3d(10, -3, 7), UpstreamAxis = new Vector3d(0, 0, 1),
			AnchorPivot = new Vector3d(12, -3, 7), AnchorAxis = new Vector3d(1, 0, 0),
			SourceOpen = new Vector3d(12, -1, 7), SourceDirection = new Vector3d(0, 1, 0),
			TargetPoint = new Vector3d(10 + 1.5 * h, -5.5, 6.5 - 1.5 * h), TargetOpenDirection = new Vector3d(-h, 0, h),
			UpstreamLength = 5, AnchorLength = 2, SourceLength = 2,
			MinimumLength = 0.1, MaximumAdjustment = 10, PositionTolerance = 1e-6
		};
		AssertTrue(DirectYArticulation.TrySolve(input, out var result, out string error), error);
		AssertNear(Degrees(-90), result.OuterRotation, 1e-6, "negative outer turn");
		AssertNear(Degrees(-45), result.InnerRotation, 1e-6, "negative inner pitch");
		AssertNear(-0.5, result.UpstreamSlide, 1e-6, "upstream shortening");
		AssertNear(0.5, result.AnchorSlide, 1e-6, "anchor lengthening");
		AssertNear(-0.5, result.SourceExtension, 1e-6, "source shortening");
		input.SourceLength = 0.2;
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "cannot reverse terminal pipe");
	}

	private static void RejectsNonfiniteArticulationLengths()
	{
		// Without finite scalar validation a NaN length bypasses all positive-length guards.
		var input = new DirectYArticulationInput
		{
			UpstreamAxis = new Vector3d(0, 0, 1), AnchorAxis = new Vector3d(1, 0, 0),
			AnchorPivot = new Vector3d(2, 0, 0), SourceOpen = new Vector3d(2, 2, 0),
			SourceDirection = new Vector3d(0, 1, 0), TargetPoint = new Vector3d(2, 2, 0),
			TargetOpenDirection = new Vector3d(0, -1, 0), UpstreamLength = 5,
			AnchorLength = 2, SourceLength = double.NaN, MinimumLength = 0.1,
			MaximumAdjustment = 10, PositionTolerance = 1e-6
		};
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "NaN source length");
		input.SourceLength = 2; input.MaximumAdjustment = double.PositiveInfinity;
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "infinite move limit");
		input.MaximumAdjustment = 10; input.PositionTolerance = double.NaN;
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "NaN tolerance");
	}

	private static void SharesParallelAxisAdjustmentWithinLimits()
	{
		// Moving only upstream OR terminal by 1.5 exceeds the limit; splitting 0.75/0.75 is valid.
		var input = new DirectYArticulationInput
		{
			UpstreamAxis = new Vector3d(0, 0, 1), AnchorAxis = new Vector3d(1, 0, 0),
			AnchorPivot = new Vector3d(2, 0, 0), SourceOpen = new Vector3d(2, 0, 2),
			SourceDirection = new Vector3d(0, 0, 1), TargetOpenDirection = new Vector3d(0, 0, -1),
			TargetPoint = new Vector3d(3, 0, 3.5), UpstreamLength = 5, AnchorLength = 2, SourceLength = 2,
			MinimumLength = 0.1, MaximumAdjustment = 1, PositionTolerance = 1e-6
		};
		AssertTrue(DirectYArticulation.TrySolve(input, out var result, out string error), error);
		AssertNear(0.75, result.UpstreamSlide, 1e-6, "shared upstream adjustment");
		AssertNear(0.75, result.SourceExtension, 1e-6, "shared source adjustment");
		AssertNear(1, result.AnchorSlide, 1e-6, "orthogonal adjustment");
		input.TargetPoint = new Vector3d(3, 0, 4.1);
		AssertFalse(DirectYArticulation.TrySolve(input, out _, out _), "combined parallel capacity exceeded");
	}

	private static void RotatesFreeOuterJointForPosition()
	{
		// Target direction is parallel to upstream: orientation leaves outer angle free, position determines it.
		var input = new DirectYArticulationInput
		{
			UpstreamAxis = new Vector3d(0, 0, 1), AnchorAxis = new Vector3d(1, 0, 0),
			AnchorPivot = new Vector3d(2, 0, 0), SourceOpen = new Vector3d(2, 0, 2),
			SourceDirection = new Vector3d(0, 0, 1), TargetOpenDirection = new Vector3d(0, 0, -1),
			TargetPoint = new Vector3d(0, 3, 2), UpstreamLength = 5, AnchorLength = 2, SourceLength = 2,
			MinimumLength = 0.1, MaximumAdjustment = 2, PositionTolerance = 1e-6
		};
		AssertTrue(DirectYArticulation.TrySolve(input, out var result, out string error), error);
		AssertNear(Degrees(90), result.OuterRotation, 1e-6, "position-driven outer rotation");
		AssertNear(0, result.InnerRotation, 1e-6, "unchanged source orientation");
		AssertNear(1, result.AnchorSlide, 1e-6, "new radial length");
	}

	private static void RejectsUnsupportedCloserTeeAndIncompleteScan()
	{
		// A perpendicular tee must not shadow the eligible angled Y; a truncated run must not be ranked.
		var candidates = new[] { new DirectYTargetCandidate(0.1, 0), new DirectYTargetCandidate(0.4, Math.Sqrt(0.5)) };
		AssertTrue(DirectYGuards.TryChooseTarget(candidates, true, 0.02, out int index, out string error), error);
		AssertEqual(1, index, "eligible Y wins over closer unsupported tee");
		AssertFalse(DirectYGuards.TryChooseTarget(candidates, false, 0.02, out _, out _), "do not use incomplete scan");
		AssertFalse(DirectYGuards.TryChooseTarget(new[] { candidates[0] }, true, 0.02, out _, out _), "no angled Y");
		AssertFalse(DirectYGuards.TryChooseTarget(new[] { candidates[1], new DirectYTargetCandidate(0.41, -0.7) },
			true, 0.02, out _, out _), "two eligible Ys equally near are ambiguous");
		AssertFalse(DirectYGuards.TryChooseTarget(new[] { new DirectYTargetCandidate(double.NaN, 0.7) },
			true, 0.02, out _, out _), "nonfinite distance");
	}

	private static void DetectsFittingShapeChangesAfterRigidMotion()
	{
		// Pure +90° rotation and translation preserve signature; takeout/angle changes must fail.
		double h = Math.Sqrt(0.5);
		var original = DirectYRigidSignature.Create(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0),
			new Vector3d(1, 0, 0), new Vector3d(-h, h, 0));
		var rigid = DirectYRigidSignature.Create(new Vector3d(5, 2, 0), new Vector3d(5, 4, 0),
			new Vector3d(0, 1, 0), new Vector3d(-h, -h, 0));
		AssertTrue(original.IsPreservedBy(rigid, 1e-6, Degrees(0.5)), "rigid shape remains equal");
		var shortened = DirectYRigidSignature.Create(new Vector3d(5, 2, 0), new Vector3d(5, 3.5, 0),
			new Vector3d(0, 1, 0), new Vector3d(-h, -h, 0));
		AssertFalse(original.IsPreservedBy(shortened, 1e-6, Degrees(0.5)), "fitting takeout changed");
		var bent = DirectYRigidSignature.Create(new Vector3d(5, 2, 0), new Vector3d(5, 4, 0),
			new Vector3d(0, 1, 0), new Vector3d(0, -1, 0));
		AssertFalse(original.IsPreservedBy(bent, 1e-6, Degrees(0.5)), "fitting bend changed");
		var skewed = DirectYRigidSignature.Create(new Vector3d(5, 2, 0), new Vector3d(7, 2, 0),
			new Vector3d(0, 1, 0), new Vector3d(-h, -h, 0));
		AssertFalse(original.IsPreservedBy(skewed, 1e-6, Degrees(0.5)), "same port distance but offset axis changed");
	}

	private static void FindsSingleJointInsertionWithoutHeightChange()
	{
		// Endpoint rotation gives (0,2,0), main line runs X at Y=3; insertion must land at X=0.
		AssertTrue(DirectYInsertionGeometry.TryFitSingle(new Vector3d(0,0,0), new Vector3d(0,0,1),
			new Vector3d(2,0,0), new Vector3d(1,0,0), new Vector3d(-5,3,0), new Vector3d(0,-1,0),
			new Vector3d(1,0,0), 0.5, 9.5, 5, 2, 0.1, 10, 1e-6, out double shift), "find insertion by rotation");
		AssertNear(5, shift, 1e-6, "insertion distance on main line");
		AssertFalse(DirectYInsertionGeometry.TryFitSingle(new Vector3d(0,0,0), new Vector3d(0,0,1),
			new Vector3d(2,0,0), new Vector3d(1,0,0), new Vector3d(-5,3,0), new Vector3d(0,-1,0),
			new Vector3d(1,0,0), 6, 9.5, 5, 2, 0.1, 10, 1e-6, out _), "cannot insert outside interior");
		var directions = DirectYInsertionGeometry.BranchDirections(new Vector3d(1,0,0),new Vector3d(0,1,0),
			new Vector3d(0,0,1),new Vector3d(1,0,0));
		AssertTrue(directions.Count > 0, "branch orientation candidates");
		foreach (var direction in directions)
		{
			AssertNear(1,direction.Length,1e-6,"unit branch direction");
			AssertNear(Math.Sqrt(0.5),Math.Abs(direction.X),1e-6,"45-degree Y branch");
		}
	}

	private static void ConstrainsArticulationHeightWhileFindingInsertion()
	{
		double h=Math.Sqrt(0.5);
		var input=new DirectYArticulationInput {
			UpstreamAxis=new Vector3d(0,0,1),AnchorAxis=new Vector3d(1,0,0),AnchorPivot=new Vector3d(2,0,0),
			SourceOpen=new Vector3d(2,2,0),SourceDirection=new Vector3d(0,1,0),
			TargetPoint=new Vector3d(3,3*h,3*h),TargetOpenDirection=new Vector3d(0,-h,-h),
			UpstreamLength=5,AnchorLength=2,SourceLength=2,MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-6,
			FixedUpstreamSlide=0,TargetSlideAxis=new Vector3d(0,0,1),TargetSlideMinimum=-2,TargetSlideMaximum=2 };
		AssertTrue(DirectYArticulation.TrySolve(input,out var result,out string error),error);
		AssertNear(0,result.UpstreamSlide,1e-6,"rotate before changing upstream height");
		AssertNear(0,result.TargetSlide,1e-6,"nominal insertion needs no shift");
		input.FixedUpstreamSlide=0.25;
		AssertTrue(DirectYArticulation.TrySolve(input,out result,out error),error);
		AssertNear(0.25,result.UpstreamSlide,1e-6,"locked small height step");
		AssertNear(0.25,result.TargetSlide,1e-6,"adjust insertion at locked height");
	}

	private static void SearchesHeightInSmallRotateFirstSteps()
	{
		var levels=DirectYInsertionGeometry.ElevationSteps(5,15);
		double[] expected={0,5,-5,10,-10,15,-15};
		AssertEqual(expected.Length,levels.Count,"bounded incremental height search");
		for(int i=0;i<expected.Length;i++) AssertNear(expected[i],levels[i],1e-9,"rotate-first height step");
	}

	private static void SearchesHalfMillimetreHeightsAcrossFullRange()
	{
		// Keeping the old 100-increment cap silently truncates 0.5mm search at ±50mm.
		var levels = DirectYInsertionGeometry.ElevationSteps(0.5,100);
		AssertEqual(401,levels.Count,"0.5mm search retains all levels through ±100mm");
		double[] first = {0,0.5,-0.5,1,-1,1.5,-1.5};
		for (int i=0;i<first.Length;i++) AssertNear(first[i],levels[i],1e-9,"smallest absolute height first, rotate at zero first");
		AssertNear(37.5,levels[149],1e-9,"includes an intermediate level below old 40mm candidate");
		AssertNear(-37.5,levels[150],1e-9,"includes matching negative intermediate level");
		AssertNear(100,levels[399],1e-9,"positive range not truncated");
		AssertNear(-100,levels[400],1e-9,"negative range not truncated");
		var feet = DirectYInsertionGeometry.ElevationSteps(0.5/304.8,100/304.8);
		AssertEqual(401,feet.Count,"native feet conversion keeps the complete half-mm range");
		AssertNear(0.5,feet[1]*304.8,1e-9,"native first height step is half a millimetre");
		AssertNear(100,feet[399]*304.8,1e-9,"native converted upper range is still 100mm");
		for (int i=1;i<levels.Count;i++)
		{
			AssertTrue(Math.Abs(levels[i])<=100,"never exceeds elevation bound");
			AssertTrue(Math.Abs(levels[i])>=Math.Abs(levels[i-1]),"does not skip to a larger elevation before a smaller one");
		}
	}

	private static void RejectsUnboundedElevationEnumeration()
	{
		AssertEqual(0,DirectYInsertionGeometry.ElevationSteps(0.0001,100).Count,"reject impractical enumeration instead of silently truncating range");
		AssertEqual(0,DirectYInsertionGeometry.ElevationSteps(1e-300,100).Count,"extremely large finite increment count cannot become truncated valid search");
		AssertEqual(0,DirectYInsertionGeometry.ElevationSteps(double.Epsilon,100).Count,"overflowing increment quotient is rejected before enumeration");
	}

	private static void LocksSingleJointHeightBeforeTryingInsertion()
	{
		AssertFalse(DirectYInsertionGeometry.TryFitSingle(new Vector3d(0,0,0),new Vector3d(0,0,1),
			new Vector3d(2,0,0),new Vector3d(1,0,0),new Vector3d(-5,3,0.25),new Vector3d(0,-1,0),
			new Vector3d(1,0,0),0.5,9.5,5,2,0.1,10,1e-6,out _,0),"zero-height pass cannot silently lift branch");
		AssertTrue(DirectYInsertionGeometry.TryFitSingle(new Vector3d(0,0,0),new Vector3d(0,0,1),
			new Vector3d(2,0,0),new Vector3d(1,0,0),new Vector3d(-5,3,0.25),new Vector3d(0,-1,0),
			new Vector3d(1,0,0),0.5,9.5,5,2,0.1,10,1e-6,out double shift,0.25),"locked height permits matching insertion");
		AssertNear(5,shift,1e-6,"locked height insertion");
	}

	private static void PreventsHiddenInnerAnchorElevation()
	{
		var input=new DirectYArticulationInput {
			UpstreamAxis=new Vector3d(1,0,0),AnchorAxis=new Vector3d(0,0,1),AnchorPivot=new Vector3d(0,0,2),
			SourceOpen=new Vector3d(0,2,2),SourceDirection=new Vector3d(0,1,0),TargetPoint=new Vector3d(0,3,3),
			TargetOpenDirection=new Vector3d(0,-1,0),UpstreamLength=5,AnchorLength=2,SourceLength=2,
			MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-6,FixedUpstreamSlide=0 };
		AssertTrue(DirectYArticulation.TrySolve(input,out var result,out string error),error);
		AssertNear(1,result.AnchorSlide,1e-6,"unconstrained anchor lifts");
		input.PreserveAnchorElevation=true;
		AssertFalse(DirectYArticulation.TrySolve(input,out _,out _),"rotate-first pass must not hide a vertical inner-anchor move");
	}

	private static void AllowsExplicitInnerHeightWithHorizontalUpstream()
	{
		var input=new DirectYArticulationInput {
			UpstreamAxis=new Vector3d(1,0,0),AnchorAxis=new Vector3d(0,0,1),AnchorPivot=new Vector3d(0,0,2),
			SourceOpen=new Vector3d(0,2,2),SourceDirection=new Vector3d(0,1,0),TargetPoint=new Vector3d(0,3,2.005),
			TargetOpenDirection=new Vector3d(0,-1,0),UpstreamLength=5,AnchorLength=2,SourceLength=2,
			MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-6,FixedUpstreamSlide=0,
			PreserveAnchorElevation=true,AnchorElevationOffset=0.005 };
		AssertTrue(DirectYArticulation.TrySolve(input,out var result,out string error),error);
		AssertNear(0,result.UpstreamSlide,1e-6,"fixed upstream stays still");
		AssertNear(0.005,result.AnchorSlide*result.TransformedAnchorAxis.Z,1e-6,"explicit incremental inner lift");
	}

	private static void FindsPositionDerivedZeroHeightRoll()
	{
		var center=new Vector3d(0,0.837117307087384,2.7927109793487);
		var input=new DirectYArticulationInput {
			UpstreamAxis=new Vector3d(1,0,0),AnchorAxis=new Vector3d(0,0,1),AnchorPivot=new Vector3d(0,0,2),
			SourceOpen=new Vector3d(0,2,2),SourceDirection=new Vector3d(0,1,0),UpstreamLength=5,AnchorLength=2,SourceLength=2,
			MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-6,
			TargetSlideAxis=new Vector3d(1,0,0),TargetSlideMinimum=-5,TargetSlideMaximum=5 };
		double h=Math.Sqrt(0.5);
		var rolls=DirectYInsertionGeometry.PositionRolls(input,new Vector3d(1,0,0),center,new Vector3d(h,-h,0),default,0);
		AssertTrue(rolls.Count>0,"position-dependent roll root exists between coarse samples");
		bool found=false;
		foreach(double roll in rolls)
		{
			input.TargetPoint=center;
			input.TargetOpenDirection=DirectYArticulation.Rotate(new Vector3d(h,-h,0),new Vector3d(1,0,0),roll);
			foreach(var result in DirectYArticulation.HeightSolutions(input,0))
				if(Math.Abs(result.OuterRotation-Math.PI/6)<1e-4 && Math.Abs(result.InnerRotation-Math.PI/4)<1e-4) found=true;
		}
		AssertTrue(found,"finds reviewed 30-degree outer/45-degree inner rotation without changing height");
	}

	private static void AccountsForRolledFittingTakeoutAndRejectsOutsideMain()
	{
		var axis=new Vector3d(1,0,0);double h=Math.Sqrt(0.5),roll=Math.PI/6;
		var offset=new Vector3d(0.12,-0.12,0);
		var actualTarget=new Vector3d(0,0.837117307087384,2.7927109793487);
		var center=actualTarget-DirectYArticulation.Rotate(offset,axis,roll);
		var input=new DirectYArticulationInput {
			UpstreamAxis=axis,AnchorAxis=new Vector3d(0,0,1),AnchorPivot=new Vector3d(0,0,2),
			SourceOpen=new Vector3d(0,2,2),SourceDirection=new Vector3d(0,1,0),UpstreamLength=5,AnchorLength=2,SourceLength=2,
			MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-6,
			TargetSlideAxis=axis,TargetSlideMinimum=-5,TargetSlideMaximum=5 };
		var rolls=DirectYInsertionGeometry.PositionRolls(input,axis,center,new Vector3d(h,-h,0),offset,0);
		AssertTrue(System.Linq.Enumerable.Any(rolls,value=>Math.Abs(value-roll)<1e-4),"fitting takeout rotates with the Y");
		input.TargetSlideMinimum=100;input.TargetSlideMaximum=110;
		AssertEqual(0,DirectYInsertionGeometry.PositionRolls(input,axis,center,new Vector3d(h,-h,0),offset,0).Count,"no root outside bounded main");
		AssertEqual(0,DirectYInsertionGeometry.PositionRolls(input,default,center,new Vector3d(h,-h,0),offset,0).Count,"invalid main axis");
	}

	private static void RefinesOffGridPositionRolls()
	{
		double h=Math.Sqrt(0.5);
		foreach(double degrees in new[]{31.0,31.234,37.0})
		{
			double angle=degrees*Math.PI/180;
			var center=new Vector3d(0,-2*Math.Sin(angle)+3*h*Math.Cos(angle),2*Math.Cos(angle)+3*h*Math.Sin(angle));
			var input=new DirectYArticulationInput {
				UpstreamAxis=new Vector3d(1,0,0),AnchorAxis=new Vector3d(0,0,1),AnchorPivot=new Vector3d(0,0,2),
				SourceOpen=new Vector3d(0,2,2),SourceDirection=new Vector3d(0,1,0),UpstreamLength=5,AnchorLength=2,SourceLength=2,
				MinimumLength=0.1,MaximumAdjustment=10,PositionTolerance=1e-7,
				TargetSlideAxis=new Vector3d(1,0,0),TargetSlideMinimum=-5,TargetSlideMaximum=5 };
			var rolls=DirectYInsertionGeometry.PositionRolls(input,new Vector3d(1,0,0),center,new Vector3d(h,-h,0),default,0);
			AssertTrue(System.Linq.Enumerable.Any(rolls,value=>Math.Abs(value-angle)<1e-5),$"refine off-grid {degrees} degree root before changing height");
		}
	}

	private static void FindsHalfDegreeRollInNarrowFeasibleDirectionWindow()
	{
		// Two 45deg bends can reach a direction perpendicular to upstream only at
		// the edge of their direction cone. This fixture's Y roll touches that edge
		// at 0.5deg; adjacent 2.5deg samples have no feasible direction to refine.
		double h = Math.Sqrt(0.5), phi = 0.5*Math.PI/180;
		var anchor = new Vector3d(0,Math.Cos(phi),Math.Sin(phi));
		var sourceDirection = new Vector3d(h,h*Math.Cos(phi),h*Math.Sin(phi));
		var upstream = new Vector3d(-h,h*Math.Cos(phi),h*Math.Sin(phi));
		var sourceOpen = anchor*2 + sourceDirection*2;
		var center = anchor*2 + sourceDirection*3;
		var main = new Vector3d(1,0,0);
		var input = new DirectYArticulationInput {
			UpstreamAxis=upstream,AnchorAxis=anchor,AnchorPivot=anchor*2,
			SourceOpen=sourceOpen,SourceDirection=sourceDirection,
			UpstreamLength=5,AnchorLength=2,SourceLength=2,MinimumLength=0.1,
			MaximumAdjustment=10,PositionTolerance=1e-6,
			TargetSlideAxis=main,TargetSlideMinimum=-5,TargetSlideMaximum=5 };
		var rolls = DirectYInsertionGeometry.PositionRolls(input,main,center,new Vector3d(-h,-h,0),default,0);
		AssertTrue(System.Linq.Enumerable.Any(rolls,roll=>Math.Abs(roll-phi)<1e-5),"0.5deg discovery finds narrow feasible roll missed by old 2.5deg grid");
		bool connectsWithoutHeight=false;
		foreach (double roll in rolls)
		{
			input.TargetPoint=center;
			input.TargetOpenDirection=DirectYArticulation.Rotate(new Vector3d(-h,-h,0),main,roll);
			foreach (var solution in DirectYArticulation.HeightSolutions(input,0))
				if (Math.Abs(solution.UpstreamSlide)<1e-9 && Math.Abs(solution.AnchorSlide)<1e-9 && solution.Residual<=1e-6)
					connectsWithoutHeight=true;
		}
		AssertTrue(connectsWithoutHeight,"fine roll provides a validated zero-height solution, not just an angle candidate");
	}

	private static void ApprovesOnlyConfirmedTemporaryDeletion()
	{
		var records=new[]{new DirectYDeletionCandidate(100,false,false,true,"Pipe thử"),
			new DirectYDeletionCandidate(101,false,false,true,"PipeInsulation thử"),
			new DirectYDeletionCandidate(102,false,false,true,"PipingSystem mới chỉ chứa phần tạm")};
		AssertTrue(DirectYCleanupPolicy.TryApprove(100,records,out string error),error);
		AssertFalse(DirectYCleanupPolicy.TryApprove(100,new[]{new DirectYDeletionCandidate(105,false,false,true,"Phần tạm khác")},out _),"stub must be in deleted set");
		AssertFalse(DirectYCleanupPolicy.TryApprove(100,new[]{records[0],records[0]},out _),"ambiguous duplicate identities rejected");
	}

	private static void RejectsDeletionOfExistingOrRetainedElements()
	{
		var stub=new DirectYDeletionCandidate(100,false,false,true,"Pipe thử");
		foreach(var unsafeElement in new[]{new DirectYDeletionCandidate(10,true,false,true,"Phần tử model cũ"),
			new DirectYDeletionCandidate(110,false,true,true,"Y cần giữ"),
			new DirectYDeletionCandidate(111,false,true,true,"Pipe chính tách mới"),
			new DirectYDeletionCandidate(112,false,false,false,"PipingSystem chứa Pipe chính"),
			new DirectYDeletionCandidate(113,false,false,false,"Phần tử mới không rõ nguồn gốc")})
		{
			AssertFalse(DirectYCleanupPolicy.TryApprove(100,new[]{stub,unsafeElement},out string error),"retain protected/original/unverified elements");
			AssertTrue(error.Contains(unsafeElement.Id.ToString()),"diagnostic identifies rejected element ID");
		}
		AssertFalse(DirectYCleanupPolicy.TryApprove(100,null,out _),"missing cleanup evidence rejected");
	}

	private static void ChecksRealDeletionAgainstApprovedProbe()
	{
		var stub=new DirectYDeletionCandidate(100,false,false,true,"Pipe thử");
		var dependency=new DirectYDeletionCandidate(101,false,false,true,"Phụ thuộc tạm");
		AssertTrue(DirectYCleanupPolicy.TryVerifyActual(100,new long[]{100,101},new[]{dependency,stub},out string error),error);
		AssertFalse(DirectYCleanupPolicy.TryVerifyActual(100,new long[]{100},new[]{stub,dependency},out _),"unapproved extra deletion rejected even when disposable");
		AssertFalse(DirectYCleanupPolicy.TryVerifyActual(100,new long[]{100,101},new[]{stub},out _),"changed deletion graph rejected");
	}

	private static void ApprovesNewCenterLineWithOneMatchingTemporaryOwnerAndNoRetainedDependency()
	{
		bool allowed=DirectYCleanupPolicy.IsDisposableCenterLine(existing:false,retained:false,
			recognizedNativeCenterLine:true,temporaryOwnerCount:1,retainedDependent:false);
		AssertTrue(allowed,"native centerline with one matching temporary root and no checked retained dependency can be disposed");
		var records=new[]{new DirectYDeletionCandidate(100,false,false,true,"Pipe thử"),
			new DirectYDeletionCandidate(101,false,false,allowed,"Center Line phụ thuộc Pipe thử")};
		AssertTrue(DirectYCleanupPolicy.TryApprove(100,records,out string error),error);
	}

	private static void RejectsCenterLineWithoutOneMatchingTemporaryOwnerOrWithRetainedDependency()
	{
		foreach(var facts in new[]{(Existing:true,Retained:false,Native:true,Owners:1,Keep:false),
			(Existing:false,Retained:true,Native:true,Owners:1,Keep:false),
			(Existing:false,Retained:false,Native:false,Owners:1,Keep:false),
			(Existing:false,Retained:false,Native:true,Owners:0,Keep:false),
			(Existing:false,Retained:false,Native:true,Owners:2,Keep:false),
			(Existing:false,Retained:false,Native:true,Owners:1,Keep:true)})
		{
			bool allowed=DirectYCleanupPolicy.IsDisposableCenterLine(facts.Existing,facts.Retained,facts.Native,facts.Owners,facts.Keep);
			AssertFalse(allowed,"old/retained/unknown/unowned/shared centerline must not be disposed");
			var records=new[]{new DirectYDeletionCandidate(100,false,false,true,"Pipe thử"),
				new DirectYDeletionCandidate(101,facts.Existing,facts.Retained,allowed,"Center Line chưa an toàn")};
			AssertFalse(DirectYCleanupPolicy.TryApprove(100,records,out _),"whole cleanup remains rejected for unsafe centerline");
		}
	}

	private static void ReportsBindingIdentityAndMeasuredGapInMillimetres()
	{
		// Omitting either port identity, using feet as mm, losing directionality, or
		// culture-dependent decimal separators makes native traces ambiguous.
		var culture = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
			string report = DirectYBindingDiagnostic.Format(42978276, 1, 42978280, 2,
				true, false, 0.25, 0.125, -1.0, 0.5 / 304.8);
			AssertTrue(report.Contains("first=42978276:1"), "identify first owner and connector");
			AssertTrue(report.Contains("second=42978280:2"), "identify second owner and connector");
			AssertTrue(report.Contains("gapMm=76.200"), "convert native feet separation to mm");
			AssertTrue(report.Contains("snapshotGapMm=38.100"), "separate captured gap from current gap");
			AssertTrue(report.Contains("toleranceMm=0.500"), "report actual positional guard tolerance");
			AssertTrue(report.Contains("forward=True; reverse=False"), "report asymmetric native connection separately");
			AssertTrue(report.Contains("axisDot=-1.000000000"), "report measured axis dot without localization ambiguity");
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
	}

	private static void ReportsMissingBindingGeometryWithoutInventingMeasurements()
	{
		string report = DirectYBindingDiagnostic.Format(12, 3, 14, 5,
			false, false, null, null, null, 0.5 / 304.8);
		AssertTrue(report.Contains("first=12:3; second=14:5"), "retain identities for missing native ports");
		AssertTrue(report.Contains("gapMm=unavailable; snapshotGapMm=unavailable"), "missing origins are not zero-gap evidence");
		AssertTrue(report.Contains("axisDot=unavailable"), "missing axes are not opposed-axis evidence");
	}

	private static void DistinguishesBarelyFailingBindingGapFromTolerance()
	{
		string report = DirectYBindingDiagnostic.Format(12, 3, 14, 5, true, true,
			0.50004 / 304.8, 0.50004 / 304.8, -1.0, 0.5 / 304.8);
		AssertTrue(report.Contains("gapMm=0.500040"), "do not round barely failing gap to accepted threshold");
		AssertTrue(report.Contains("toleranceMm=0.500000"), "show tolerance at same precision as measured gap");
	}

	private static void RunsDiagnosticMeasurementAndContainsOrdinaryGetterFailure()
	{
		bool measured = false;
		DirectYBindingDiagnostic.Observe(() => measured = true, _ => false,
			_ => throw new InvalidOperationException("successful observation should not report unavailable"));
		AssertTrue(measured, "invoke real diagnostic observation");
		var getterError = new InvalidOperationException("native metadata unavailable");
		Exception reported = null;
		DirectYBindingDiagnostic.Observe(() => throw getterError, _ => false, error => reported = error);
		AssertTrue(object.ReferenceEquals(getterError, reported), "ordinary metadata failure produces unavailable report without aborting normal planning");
	}

	private static void RethrowsFatalDiagnosticFailure()
	{
		var fatal = new InvalidOperationException("simulated regeneration failure boundary");
		bool reported = false;
		Exception caught = null;
		try
		{
			DirectYBindingDiagnostic.Observe(() => throw fatal, error => object.ReferenceEquals(error, fatal),
				_ => reported = true);
		}
		catch (Exception error) { caught = error; }
		AssertTrue(object.ReferenceEquals(fatal, caught), "fatal native failure must escape to owning rollback path");
		AssertFalse(reported, "fatal failure must not be downgraded to an unavailable diagnostic");
	}

	private static void PreservesOnlyOriginalInternalOffsets()
	{
		AssertTrue(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,true,2.101722,-1,0.5), "original uniquely connected same-inner-cluster reducer offset may be preserved");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(false,true,true,true,2.101722,-1,0.5), "fixed/moving boundary cannot grandfather offset");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,false,true,true,2.101722,-1,0.5), "post-insertion drift cannot become a new baseline");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,false,true,2.101722,-1,0.5), "one-way or extra-peer topology cannot qualify");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,false,2.101722,-1,0.5), "mismatched profile or DN cannot qualify");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,true,0,-1,0.5), "new gap cannot be authorized for originally coincident pair");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,true,2.101722,0,0.5), "bad original axes remain rejected");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,true,double.NaN,-1,0.5), "missing original gap is not evidence");
		AssertFalse(DirectYInternalBindingGeometry.AllowsExistingOffset(true,true,true,true,2.101722,double.NaN,0.5), "missing original axis is not evidence");
	}

	private static void TransformsInternalPortsInNativeOuterThenInnerOrder()
	{
		var z = new Vector3d(0,0,1); var y = new Vector3d(0,1,0); var x = new Vector3d(1,0,0);
		var single = new DirectYRigidStep(new Vector3d(10,20,30), z, Math.PI/2, 3);
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(new Vector3d(11,20,30), x, null, single, out var p, out var a), "single rigid motion valid");
		AssertVector(new Vector3d(10,21,33),p,1e-9,"pivoted point and axial slide");
		AssertVector(y,a,1e-9,"direction rotates without translation");
		var outer = new DirectYRigidStep(new Vector3d(0,0,0), z, Math.PI/2, 2);
		var inner = new DirectYRigidStep(new Vector3d(0,0,2), y, Math.PI/2, 3);
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(new Vector3d(1,0,0), x, outer, inner, out p, out a), "composed motion valid");
		AssertVector(new Vector3d(0,4,2),p,1e-9,"outer then inner point order");
		AssertVector(y,a,1e-9,"outer then inner axis order");
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(new Vector3d(1,0.002,0), -x, outer, inner, out p, out a), "paired port composed motion valid");
		AssertVector(new Vector3d(0,4,2.002),p,1e-9,"relative offset follows composed motion");
		AssertVector(-y,a,1e-9,"paired axis stays opposed");
		AssertFalse(DirectYInternalBindingGeometry.TryTransform(x,x,null,new DirectYRigidStep(default,default,0,0),out _,out _),"invalid rotation axis rejected");
		AssertFalse(DirectYInternalBindingGeometry.TryTransform(x,x,null,new DirectYRigidStep(default,z,double.NaN,0),out _,out _),"invalid angle rejected");
	}

	private static void RejectsInternalPortAndOffsetDrift()
	{
		var x = new Vector3d(1,0,0); var zero = new Vector3d(0,0,0);
		AssertTrue(DirectYInternalBindingGeometry.MatchesPort(zero,x,zero,x,0.1,Math.PI/360),"source near end stays fixed while open end may change");
		AssertFalse(DirectYInternalBindingGeometry.MatchesPort(new Vector3d(0.11,0,0),x,zero,x,0.1,Math.PI/360),"source near end drift cannot be hidden by resizing");
		AssertFalse(DirectYInternalBindingGeometry.MatchesPort(zero,new Vector3d(Math.Cos(Math.PI/180),Math.Sin(Math.PI/180),0),zero,x,0.1,Math.PI/360),"unexpected source near axis change rejected");
		var offset = new Vector3d(0,2.101722,0);
		AssertTrue(DirectYInternalBindingGeometry.MatchesOffset(zero,offset,zero,offset,0.1),"same original offset retained");
		AssertFalse(DirectYInternalBindingGeometry.MatchesOffset(zero,new Vector3d(0,2.211722,0),zero,offset,0.1),"offset growth rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesOffset(zero,new Vector3d(0,1.991722,0),zero,offset,0.1),"offset shrinkage is not silent repair");
		AssertFalse(DirectYInternalBindingGeometry.MatchesOffset(zero,new Vector3d(0,0,2.101722),zero,offset,0.1),"equal gap with changed azimuth rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesOffset(zero,new Vector3d(0,0.11,0),zero,zero,0.1),"new gap on coincident baseline rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesPort(new Vector3d(double.NaN,0,0),x,zero,x,0.1,Math.PI/360),"unavailable current geometry rejected");
	}

	private static void TransformsMeasuredReducerOffsetWithoutChangingIt()
	{
		// Hand-derived 90deg Z rotation of the native log's transverse delta, in mm.
		var delta = new Vector3d(-1.350861,1.356989,-0.866596);
		var axis = new Vector3d(0.707097943,0.707097943,0.004999938);
		var motion = new DirectYRigidStep(default,new Vector3d(0,0,1),Math.PI/2,0);
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(delta,axis,null,motion,out var p,out _),"actual reducer delta can follow rigid rotation");
		AssertVector(new Vector3d(-1.356989,-1.350861,-0.866596),p,1e-9,"native reducer delta rotates without repair");
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(-delta,-axis,null,motion,out p,out _),"opposite reducer-side delta follows same motion");
		AssertVector(new Vector3d(1.356989,1.350861,0.866596),p,1e-9,"opposite reducer delta remains opposite");
	}

	private static void RejectsUncommandedFittingRollWithUnchangedConnectorAxis()
	{
		var x = new Vector3d(1,0,0); var y = new Vector3d(0,1,0); var z = new Vector3d(0,0,1);
		// Expected frame after outer Z90 then inner Y90: X→Y, Y→Z, Z→X.
		// Rolling this fitting around its Z'=X line leaves connector origins/Z' unchanged.
		var outer = new DirectYRigidStep(default,z,Math.PI/2,2);
		var inner = new DirectYRigidStep(new Vector3d(0,0,2),y,Math.PI/2,3);
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(x,y,outer,inner,out _,out var transformedY), "fitting Y axis transforms through both rotations");
		AssertVector(z,transformedY,1e-9,"expected fitting Y→Z");
		AssertTrue(DirectYInternalBindingGeometry.TryTransform(x,z,outer,inner,out _,out var transformedZ), "fitting Z axis transforms through both rotations");
		AssertVector(x,transformedZ,1e-9,"expected fitting Z→X");
		AssertTrue(DirectYInternalBindingGeometry.MatchesFittingFrame(y,z,y,z,Math.PI/360), "correct rotated fitting frame accepted");
		AssertFalse(DirectYInternalBindingGeometry.MatchesFittingFrame(z,-y,y,z,Math.PI/360), "uncommanded fitting body roll rejected even if connector Z stays X");
		AssertFalse(DirectYInternalBindingGeometry.MatchesFittingFrame(y,-z,y,z,Math.PI/360), "changed second transverse axis rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesFittingFrame(default,z,y,z,Math.PI/360), "zero fitting transverse axis rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesFittingFrame(new Vector3d(double.NaN,0,0),z,y,z,Math.PI/360), "missing fitting frame rejected");
		AssertFalse(DirectYInternalBindingGeometry.MatchesFittingFrame(y,z,y,z,0), "invalid frame tolerance rejected");
		AssertTrue(DirectYInternalBindingGeometry.MatchesPort(default,x,default,x,0.1,Math.PI/360), "existing Z-only guard cannot observe axial fitting roll");
	}

	private static DirectYGeometryInput Input(
		Vector3d pivot, Vector3d anchor, Vector3d sourceOpen, Vector3d sourceAxis,
		Vector3d targetPoint, Vector3d targetOpen)
	{
		return new DirectYGeometryInput(pivot, anchor, sourceOpen, sourceAxis,
			targetPoint, targetOpen, AngularTolerance, PositionTolerance);
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

	private static void AssertEqual<T>(T expected, T actual, string message)
	{
		_assertions++;
		if (!object.Equals(expected, actual))
		{
			throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
		}
	}

	private static void AssertNear(double expected, double actual, double tolerance, string message)
	{
		_assertions++;
		if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
		{
			throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
		}
	}

	private static void AssertVector(Vector3d expected, Vector3d actual, double tolerance, string message)
	{
		AssertNear(expected.X, actual.X, tolerance, message + " X");
		AssertNear(expected.Y, actual.Y, tolerance, message + " Y");
		AssertNear(expected.Z, actual.Z, tolerance, message + " Z");
	}
}
