using System;
using System.Collections.Generic;
using VP_RevitAPI.Commands.FF;

internal static class AutoTagPipeCollisionLayoutConsoleTest
{
    private static int _assertions;

    public static int Main()
    {
        try
        {
            KeepsMidpointWhenClear();
            MovesAwayFromAnOverlappingTag();
            ExpandsBeyondACrowdedLocalArea();
            PacksManyTagsWithoutPairwiseOverlap();

            Console.WriteLine(
                "AutoTagPipe collision layout regression passed: " +
                _assertions + " assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                "AutoTagPipe collision layout regression failed:");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void KeepsMidpointWhenClear()
    {
        AutoTagPipePlacement placement = Find(
            new List<AutoTagPipeRectangle>());

        AssertTrue(placement.Found, "Clear midpoint must produce a placement");
        AssertNear(placement.Head.X, 0.0, "Clear midpoint X");
        AssertNear(placement.Head.Y, 0.0, "Clear midpoint Y");
        AssertTrue(!placement.WasMoved, "Clear midpoint must not be moved");
    }

    private static void MovesAwayFromAnOverlappingTag()
    {
        var occupied = new List<AutoTagPipeRectangle>
        {
            new AutoTagPipeRectangle(-2.25, -1.25, 2.25, 1.25)
        };

        AutoTagPipePlacement placement = Find(occupied);
        AutoTagPipeRectangle placedBounds = TagOffsets().Translate(placement.Head);

        AssertTrue(placement.Found, "Overlapping midpoint must find another lane");
        AssertTrue(placement.WasMoved, "Overlapping midpoint must be moved");
        AssertTrue(
            !placedBounds.Inflate(0.25).Intersects(occupied[0]),
            "Moved tag must preserve the requested gap");
    }

    private static void ExpandsBeyondACrowdedLocalArea()
    {
        var occupied = new List<AutoTagPipeRectangle>
        {
            new AutoTagPipeRectangle(-14.0, -10.0, 14.0, 10.0)
        };

        AutoTagPipePlacement placement = Find(occupied);
        AutoTagPipeRectangle placedBounds = TagOffsets().Translate(placement.Head);

        AssertTrue(placement.Found, "Crowded local area must expand to another row");
        AssertTrue(
            !placedBounds.Inflate(0.25).Intersects(occupied[0]),
            "Expanded row must not retain a collision fallback");
    }

    private static void PacksManyTagsWithoutPairwiseOverlap()
    {
        var occupied = new List<AutoTagPipeRectangle>();
        for (int index = 0; index < 20; index++)
        {
            AutoTagPipePlacement placement = Find(occupied);
            AssertTrue(placement.Found, "Dense tag set must always find a free row");
            AutoTagPipeRectangle placedBounds = TagOffsets()
                .Translate(placement.Head)
                .Inflate(0.25);
            foreach (AutoTagPipeRectangle existing in occupied)
            {
                AssertTrue(
                    !placedBounds.Intersects(existing),
                    "New tag must not overlap a previously packed tag");
            }
            occupied.Add(placedBounds);
        }
    }

    private static AutoTagPipePlacement Find(
        IReadOnlyList<AutoTagPipeRectangle> occupied)
    {
        return AutoTagPipeCollisionLayout.FindNearestFreeHead(
            TagOffsets(),
            new AutoTagPipePoint(0.0, 0.0),
            new AutoTagPipePoint(1.0, 0.0),
            occupied,
            0.5,
            128);
    }

    private static AutoTagPipeRectangle TagOffsets()
    {
        return new AutoTagPipeRectangle(-2.0, -1.0, 2.0, 1.0);
    }

    private static void AssertNear(double actual, double expected, string message)
    {
        AssertTrue(Math.Abs(actual - expected) <= 1e-8, message);
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
