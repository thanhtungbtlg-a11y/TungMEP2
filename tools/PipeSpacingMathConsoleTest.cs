using System;
using System.Collections.Generic;
using VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal static class PipeSpacingMathConsoleTest
{
    private static int _assertions;

    public static int Main()
    {
        try
        {
            BuildsCaseStudyClearances();
            AnchorsLargerRightOuterPipe();
            AnchorsLargerLeftOuterPipe();
            UsesLowerCoordinateForEqualOuterDiameters();
            BuildsEqualCenterSpacing();
            SelectsDnRuleAndFallback();
            RejectsNegativeOrDegenerateInput();
            PartitionsPassThroughChain();
            StopsAtBoundaryAndMarksStretchPipe();
            VisitsCycleOnce();
            ConstrainsDirectBoundarySeed();
            ConstrainsTwoFixedBoundaries();
            StopsAtUnsupportedOwner();
            DetectsOverlappingClusterOwnership();
            AcceptsDisjointClusterOwnership();
            CreatesVersionedDefaultSettings();
            CreatesVisibleDefaultDnRuleTable();
            RestoresDefaultDnRuleTableWhenSavedRulesAreEmpty();
            NormalizesInvalidSettingsWithoutMutatingInput();
            NormalizesDnRulesDeterministically();

            Console.WriteLine(
                "PipeSpacing math tests passed: " + _assertions + " assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("PipeSpacing math tests failed:");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void BuildsCaseStudyClearances()
    {
        PipeSpacingScalarPlan plan = Build(
            new[]
            {
                Input(1, 0, 110, 100),
                Input(2, 190, 110, 100),
                Input(3, 430, 220, 200),
                Input(4, 725, 220, 200)
            },
            PipeSpacingMode.EqualClearGap,
            75,
            Array.Empty<PipeSpacingDnRule>(),
            75);

        AssertNear(185, plan.Targets[1].TargetCoordinate - plan.Targets[0].TargetCoordinate, "DN100-DN100 center distance");
        AssertNear(240, plan.Targets[2].TargetCoordinate - plan.Targets[1].TargetCoordinate, "DN100-DN200 center distance");
        AssertNear(295, plan.Targets[3].TargetCoordinate - plan.Targets[2].TargetCoordinate, "DN200-DN200 center distance");
        AssertNear(75, plan.MinimumClearGap, "Case-study minimum clear gap");
    }

    private static void AnchorsLargerRightOuterPipe()
    {
        PipeSpacingScalarPlan plan = Build(
            new[] { Input(1, 0, 110, 100), Input(2, 300, 220, 200) },
            PipeSpacingMode.EqualClearGap,
            75,
            Array.Empty<PipeSpacingDnRule>(),
            75);

        AssertEqual(1, plan.AnchorIndex, "Right outer pipe must anchor");
        AssertTrue(plan.Targets[1].IsAnchor, "Right target must be anchor");
        AssertNear(300, plan.Targets[1].TargetCoordinate, "Right anchor coordinate");
    }

    private static void AnchorsLargerLeftOuterPipe()
    {
        PipeSpacingScalarPlan plan = Build(
            new[]
            {
                Input(1, -50, 300, 250),
                Input(2, 250, 110, 100),
                Input(3, 500, 220, 200)
            },
            PipeSpacingMode.EqualClearGap,
            50,
            Array.Empty<PipeSpacingDnRule>(),
            50);

        AssertEqual(0, plan.AnchorIndex, "Left outer pipe must anchor");
        AssertNear(-50, plan.Targets[0].TargetCoordinate, "Left anchor coordinate");
        AssertTrue(plan.Targets[1].TargetCoordinate > plan.Targets[0].TargetCoordinate, "Targets must grow away from left anchor");
    }

    private static void UsesLowerCoordinateForEqualOuterDiameters()
    {
        PipeSpacingScalarPlan plan = Build(
            new[]
            {
                Input(10, -100, 110, 100),
                Input(11, 0, 220, 200),
                Input(12, 100, 110, 100)
            },
            PipeSpacingMode.EqualClearGap,
            75,
            Array.Empty<PipeSpacingDnRule>(),
            75);

        AssertEqual(0, plan.AnchorIndex, "Equal outer diameters must anchor lower coordinate");
        AssertEqual(10L, plan.Targets[0].Key, "Tie anchor key");
    }

    private static void BuildsEqualCenterSpacing()
    {
        PipeSpacingScalarPlan plan = Build(
            new[]
            {
                Input(1, 0, 220, 200),
                Input(2, 100, 110, 100),
                Input(3, 250, 110, 100)
            },
            PipeSpacingMode.EqualCenterSpacing,
            200,
            Array.Empty<PipeSpacingDnRule>(),
            75);

        AssertNear(200, plan.Targets[1].TargetCoordinate - plan.Targets[0].TargetCoordinate, "First equal-center distance");
        AssertNear(200, plan.Targets[2].TargetCoordinate - plan.Targets[1].TargetCoordinate, "Second equal-center distance");
        AssertNear(35, plan.MinimumClearGap, "Minimum actual clear gap in center mode");
    }

    private static void SelectsDnRuleAndFallback()
    {
        var rules = new[]
        {
            new PipeSpacingDnRule(100, 50),
            new PipeSpacingDnRule(200, 75)
        };
        PipeSpacingScalarPlan plan = Build(
            new[]
            {
                Input(1, 0, 110, 100),
                Input(2, 200, 220, 200),
                Input(3, 500, 300, 250)
            },
            PipeSpacingMode.DnRuleTable,
            0,
            rules,
            100);

        AssertNear(240, plan.Targets[1].TargetCoordinate - plan.Targets[0].TargetCoordinate, "DN200 band center distance");
        AssertNear(360, plan.Targets[2].TargetCoordinate - plan.Targets[1].TargetCoordinate, "Fallback center distance");
        AssertNear(75, plan.PairClearGaps[0], "DN200 selected gap");
        AssertNear(100, plan.PairClearGaps[1], "Fallback selected gap");
    }

    private static void RejectsNegativeOrDegenerateInput()
    {
        AssertRejected(
            new[] { Input(1, 0, 110, 100) },
            PipeSpacingMode.EqualClearGap,
            75,
            "At least two inputs");
        AssertRejected(
            new[] { Input(1, 0, -1, 100), Input(2, 200, 110, 100) },
            PipeSpacingMode.EqualClearGap,
            75,
            "Negative diameter");
        AssertRejected(
            new[] { Input(1, 0, 110, 100), Input(2, 200, 110, 100) },
            PipeSpacingMode.EqualClearGap,
            -5,
            "Negative clear gap");
        AssertRejected(
            new[] { Input(1, 100, 110, 100), Input(2, 100, 110, 100) },
            PipeSpacingMode.EqualClearGap,
            75,
            "Duplicate coordinates");
    }

    private static void PartitionsPassThroughChain()
    {
        PipeSpacingTraversalPartition partition = Partition(
            1,
            Node(1, PipeSpacingTraversalNodeKind.Pipe, 2),
            Node(2, PipeSpacingTraversalNodeKind.TwoPortFitting, 1, 3),
            Node(3, PipeSpacingTraversalNodeKind.Pipe, 2));

        AssertSequence(new long[] { 1, 2, 3 }, partition.RigidIds, "Pass-through rigid ids");
        AssertEqual(0, partition.BoundaryStretchIds.Count, "Pass-through stretch count");
        AssertTrue(!partition.IsConstrained, "Open pass-through chain must be movable");
    }

    private static void StopsAtBoundaryAndMarksStretchPipe()
    {
        PipeSpacingTraversalPartition partition = Partition(
            1,
            Node(1, PipeSpacingTraversalNodeKind.Pipe, 2),
            Node(2, PipeSpacingTraversalNodeKind.TwoPortFitting, 1, 3),
            Node(3, PipeSpacingTraversalNodeKind.Pipe, 2, 4),
            Node(4, PipeSpacingTraversalNodeKind.Boundary, 3, 5),
            Node(5, PipeSpacingTraversalNodeKind.Pipe, 4));

        AssertSequence(new long[] { 1, 2 }, partition.RigidIds, "Boundary rigid ids");
        AssertSequence(new long[] { 3 }, partition.BoundaryStretchIds, "Boundary stretch pipe");
        AssertSequence(new long[] { 4 }, partition.BoundaryIds, "Boundary owner ids");
        AssertTrue(!partition.IsConstrained, "Chain with one stretch segment must be movable");
    }

    private static void VisitsCycleOnce()
    {
        PipeSpacingTraversalPartition partition = Partition(
            10,
            Node(10, PipeSpacingTraversalNodeKind.Pipe, 11, 12),
            Node(11, PipeSpacingTraversalNodeKind.TwoPortFitting, 10, 12),
            Node(12, PipeSpacingTraversalNodeKind.Pipe, 10, 11));

        AssertSequence(new long[] { 10, 11, 12 }, partition.RigidIds, "Cycle must visit each id once");
        AssertTrue(!partition.IsConstrained, "Closed pass-through cycle must not duplicate ids");
    }

    private static void ConstrainsDirectBoundarySeed()
    {
        PipeSpacingTraversalPartition partition = Partition(
            20,
            Node(20, PipeSpacingTraversalNodeKind.Pipe, 21),
            Node(21, PipeSpacingTraversalNodeKind.Boundary, 20));

        AssertSequence(new long[] { 20 }, partition.BoundaryStretchIds, "Direct-boundary seed becomes stretch");
        AssertTrue(partition.IsConstrained, "Direct-boundary seed has no rigid core");
        AssertTrue(!string.IsNullOrWhiteSpace(partition.ConstraintReason), "Constraint must explain direct boundary");
    }

    private static void ConstrainsTwoFixedBoundaries()
    {
        PipeSpacingTraversalPartition partition = Partition(
            33,
            Node(30, PipeSpacingTraversalNodeKind.Boundary, 31),
            Node(31, PipeSpacingTraversalNodeKind.Pipe, 30, 32),
            Node(32, PipeSpacingTraversalNodeKind.TwoPortFitting, 31, 33),
            Node(33, PipeSpacingTraversalNodeKind.Pipe, 32, 34),
            Node(34, PipeSpacingTraversalNodeKind.TwoPortFitting, 33, 35),
            Node(35, PipeSpacingTraversalNodeKind.Pipe, 34, 36),
            Node(36, PipeSpacingTraversalNodeKind.Boundary, 35));

        AssertSequence(new long[] { 31, 35 }, partition.BoundaryStretchIds, "Two boundary stretch pipes");
        AssertTrue(!partition.IsConstrained, "Two independent stretch segments may move one rigid Pipe core");
    }

    private static void StopsAtUnsupportedOwner()
    {
        PipeSpacingTraversalPartition partition = Partition(
            40,
            Node(40, PipeSpacingTraversalNodeKind.Pipe, 41),
            Node(41, PipeSpacingTraversalNodeKind.Unsupported, 40, 42),
            Node(42, PipeSpacingTraversalNodeKind.Pipe, 41));

        AssertSequence(new long[] { 41 }, partition.BoundaryIds, "Unsupported owner acts as boundary");
        AssertTrue(partition.IsConstrained, "Seed directly touching unsupported owner is constrained");
    }

    private static void DetectsOverlappingClusterOwnership()
    {
        var sets = new[]
        {
            new PipeSpacingOwnershipSet(10, new long[] { 10, 11, 12 }),
            new PipeSpacingOwnershipSet(20, new long[] { 20, 12, 21 })
        };
        bool found = PipeSpacingAnalysisPolicy.TryFindOwnershipOverlap(
            sets,
            out long first,
            out long second);
        AssertTrue(found, "Shared owner must be detected");
        AssertEqual(10L, first, "First overlapping cluster key");
        AssertEqual(20L, second, "Second overlapping cluster key");
    }

    private static void AcceptsDisjointClusterOwnership()
    {
        var sets = new[]
        {
            new PipeSpacingOwnershipSet(10, new long[] { 10, 11, 12 }),
            new PipeSpacingOwnershipSet(20, new long[] { 20, 21, 22 })
        };
        bool found = PipeSpacingAnalysisPolicy.TryFindOwnershipOverlap(
            sets,
            out _,
            out _);
        AssertTrue(!found, "Disjoint clusters must not be rejected");
    }

    private static void CreatesVersionedDefaultSettings()
    {
        PipeSpacingSettings settings = PipeSpacingSettings.CreateDefault();
        AssertEqual(1, settings.Version, "Default settings version");
        AssertEqual(PipeSpacingMode.EqualClearGap, settings.Mode, "Default mode");
        AssertNear(75, settings.ClearGapMm, "Default clear gap");
        AssertTrue(settings.PreviewBeforeApply, "Preview must default on");
    }

    private static void CreatesVisibleDefaultDnRuleTable()
    {
        PipeSpacingSettings settings = PipeSpacingSettings.CreateDefault();
        AssertEqual(1, settings.DnRules.Count, "Default DN rule table must be visible");
        AssertNear(1000, settings.DnRules[0].MaxNominalDiameter, "Default DN band maximum");
        AssertNear(75, settings.DnRules[0].ClearGap, "Default DN band clear gap");
        AssertNear(75, settings.FallbackClearGapMm, "Default DN fallback clear gap");
    }

    private static void RestoresDefaultDnRuleTableWhenSavedRulesAreEmpty()
    {
        var saved = new PipeSpacingSettings
        {
            Mode = PipeSpacingMode.DnRuleTable,
            FallbackClearGapMm = 90,
            DnRules = Array.Empty<PipeSpacingDnRule>()
        };

        PipeSpacingSettings normalized = saved.Normalize();
        AssertEqual(1, normalized.DnRules.Count, "Empty saved DN rules must restore the default table");
        AssertNear(1000, normalized.DnRules[0].MaxNominalDiameter, "Restored DN band maximum");
        AssertNear(75, normalized.DnRules[0].ClearGap, "Restored DN band clear gap");
        AssertNear(90, normalized.FallbackClearGapMm, "User fallback gap must be preserved");
    }

    private static void NormalizesInvalidSettingsWithoutMutatingInput()
    {
        var input = new PipeSpacingSettings
        {
            Version = -10,
            Mode = (PipeSpacingMode)999,
            ClearGapMm = -1,
            CenterSpacingMm = double.NaN,
            FallbackClearGapMm = double.PositiveInfinity,
            ParallelAngleToleranceDegrees = 50,
            PreviewBeforeApply = false
        };
        PipeSpacingSettings normalized = input.Normalize();
        AssertEqual(-10, input.Version, "Normalize must not mutate input version");
        AssertNear(-1, input.ClearGapMm, "Normalize must not mutate input gap");
        AssertEqual(1, normalized.Version, "Normalized version");
        AssertEqual(PipeSpacingMode.EqualClearGap, normalized.Mode, "Invalid mode fallback");
        AssertNear(75, normalized.ClearGapMm, "Invalid clear gap fallback");
        AssertNear(200, normalized.CenterSpacingMm, "Invalid center spacing fallback");
        AssertNear(75, normalized.FallbackClearGapMm, "Invalid fallback gap");
        AssertNear(10, normalized.ParallelAngleToleranceDegrees, "Angle tolerance clamp");
        AssertTrue(!normalized.PreviewBeforeApply, "Valid false preview setting is retained");
    }

    private static void NormalizesDnRulesDeterministically()
    {
        var input = new PipeSpacingSettings
        {
            DnRules = new[]
            {
                new PipeSpacingDnRule(200, 75),
                new PipeSpacingDnRule(-1, 10),
                new PipeSpacingDnRule(100, 50),
                new PipeSpacingDnRule(100, 60),
                new PipeSpacingDnRule(300, -5)
            }
        };
        PipeSpacingSettings normalized = input.Normalize();
        AssertEqual(2, normalized.DnRules.Count, "Only valid unique DN rules remain");
        AssertNear(100, normalized.DnRules[0].MaxNominalDiameter, "DN rules sorted ascending");
        AssertNear(60, normalized.DnRules[0].ClearGap, "Last duplicate DN rule wins");
        AssertNear(200, normalized.DnRules[1].MaxNominalDiameter, "Second DN band");
    }

    private static PipeSpacingTraversalNode Node(
        long id,
        PipeSpacingTraversalNodeKind kind,
        params long[] neighbors)
    {
        return new PipeSpacingTraversalNode(id, kind, neighbors);
    }

    private static PipeSpacingTraversalPartition Partition(
        long seedId,
        params PipeSpacingTraversalNode[] nodes)
    {
        var graph = new Dictionary<long, PipeSpacingTraversalNode>();
        foreach (PipeSpacingTraversalNode node in nodes)
        {
            graph[node.Id] = node;
        }
        bool success = PipeSpacingTraversalPolicy.TryPartition(
            seedId,
            id => graph[id],
            out PipeSpacingTraversalPartition partition,
            out string error);
        if (!success)
        {
            throw new InvalidOperationException("Expected graph partition: " + error);
        }
        return partition;
    }

    private static void AssertSequence(
        IReadOnlyList<long> expected,
        IReadOnlyList<long> actual,
        string message)
    {
        AssertEqual(expected.Count, actual.Count, message + " count");
        for (int index = 0; index < expected.Count; index++)
        {
            AssertEqual(expected[index], actual[index], message + " index " + index);
        }
    }

    private static PipeSpacingScalarInput Input(long key, double coordinate, double od, double dn)
    {
        return new PipeSpacingScalarInput(key, coordinate, od, dn);
    }

    private static PipeSpacingScalarPlan Build(
        IReadOnlyList<PipeSpacingScalarInput> inputs,
        PipeSpacingMode mode,
        double requestedSpacing,
        IReadOnlyList<PipeSpacingDnRule> rules,
        double fallback)
    {
        if (!PipeSpacingMath.TryBuildTargets(
                inputs,
                mode,
                requestedSpacing,
                rules,
                fallback,
                out PipeSpacingScalarPlan plan,
                out string error))
        {
            throw new InvalidOperationException("Expected a plan: " + error);
        }
        return plan;
    }

    private static void AssertRejected(
        IReadOnlyList<PipeSpacingScalarInput> inputs,
        PipeSpacingMode mode,
        double requestedSpacing,
        string message)
    {
        bool success = PipeSpacingMath.TryBuildTargets(
            inputs,
            mode,
            requestedSpacing,
            Array.Empty<PipeSpacingDnRule>(),
            75,
            out _,
            out string error);
        AssertTrue(!success, message + " must be rejected");
        AssertTrue(!string.IsNullOrWhiteSpace(error), message + " must explain rejection");
    }

    private static void AssertNear(double expected, double actual, string message)
    {
        AssertTrue(Math.Abs(expected - actual) <= 1e-8, message + $". Expected={expected} Actual={actual}");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        AssertTrue(EqualityComparer<T>.Default.Equals(expected, actual), message + $". Expected={expected} Actual={actual}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message + ".");
        }
    }
}
