using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Commands.FF;

internal struct AutoTagPipePoint
{
	internal AutoTagPipePoint(double x, double y)
	{
		X = x;
		Y = y;
	}

	internal double X;

	internal double Y;

	internal double Length => Math.Sqrt(X * X + Y * Y);

	public static AutoTagPipePoint operator +(AutoTagPipePoint left, AutoTagPipePoint right)
	{
		return new AutoTagPipePoint(left.X + right.X, left.Y + right.Y);
	}

	public static AutoTagPipePoint operator *(AutoTagPipePoint value, double factor)
	{
		return new AutoTagPipePoint(value.X * factor, value.Y * factor);
	}
}

internal struct AutoTagPipeRectangle
{
	internal AutoTagPipeRectangle(double minX, double minY, double maxX, double maxY)
	{
		MinX = Math.Min(minX, maxX);
		MinY = Math.Min(minY, maxY);
		MaxX = Math.Max(minX, maxX);
		MaxY = Math.Max(minY, maxY);
	}

	internal double MinX;

	internal double MinY;

	internal double MaxX;

	internal double MaxY;

	internal double Width => MaxX - MinX;

	internal double Height => MaxY - MinY;

	internal AutoTagPipeRectangle Translate(AutoTagPipePoint point)
	{
		return new AutoTagPipeRectangle(
			MinX + point.X,
			MinY + point.Y,
			MaxX + point.X,
			MaxY + point.Y);
	}

	internal AutoTagPipeRectangle Inflate(double distance)
	{
		return new AutoTagPipeRectangle(
			MinX - distance,
			MinY - distance,
			MaxX + distance,
			MaxY + distance);
	}

	internal bool Intersects(AutoTagPipeRectangle other)
	{
		return MinX < other.MaxX &&
			MaxX > other.MinX &&
			MinY < other.MaxY &&
			MaxY > other.MinY;
	}
}

internal struct AutoTagPipePlacement
{
	internal AutoTagPipePlacement(bool found, AutoTagPipePoint head, bool wasMoved)
	{
		Found = found;
		Head = head;
		WasMoved = wasMoved;
	}

	internal bool Found;

	internal AutoTagPipePoint Head;

	internal bool WasMoved;
}

internal static class AutoTagPipeCollisionLayout
{
	private const double Epsilon = 1E-09;

	internal static AutoTagPipePlacement FindNearestFreeHead(
		AutoTagPipeRectangle tagOffsets,
		AutoTagPipePoint preferredHead,
		AutoTagPipePoint pipeAlongDirection,
		IReadOnlyList<AutoTagPipeRectangle> occupied,
		double gap,
		int maximumRings)
	{
		if (tagOffsets.Width <= Epsilon ||
			tagOffsets.Height <= Epsilon ||
			pipeAlongDirection.Length <= Epsilon ||
			maximumRings < 0)
		{
			return new AutoTagPipePlacement(false, preferredHead, false);
		}

		double safeGap = Math.Max(0.0, gap);
		AutoTagPipePoint along = Normalize(pipeAlongDirection);
		AutoTagPipePoint across = new AutoTagPipePoint(-along.Y, along.X);
		double alongStep = CalculateSeparationStep(tagOffsets, along, safeGap);
		double acrossStep = CalculateSeparationStep(tagOffsets, across, safeGap);
		IReadOnlyList<AutoTagPipeRectangle> obstacles =
			occupied ?? Array.Empty<AutoTagPipeRectangle>();

		for (int ring = 0; ring <= maximumRings; ring++)
		{
			foreach (GridOffset offset in EnumerateRing(ring, alongStep, acrossStep))
			{
				AutoTagPipePoint head = preferredHead +
					along * (offset.Along * alongStep) +
					across * (offset.Across * acrossStep);
				AutoTagPipeRectangle candidate = tagOffsets.Translate(head);
				if (HasClearance(candidate, obstacles, safeGap))
				{
					return new AutoTagPipePlacement(
						true,
						head,
						ring != 0);
				}
			}
		}

		return new AutoTagPipePlacement(false, preferredHead, false);
	}

	private static bool HasClearance(
		AutoTagPipeRectangle candidate,
		IReadOnlyList<AutoTagPipeRectangle> occupied,
		double gap)
	{
		double halfGap = gap * 0.5;
		AutoTagPipeRectangle expandedCandidate = candidate.Inflate(halfGap);
		foreach (AutoTagPipeRectangle obstacle in occupied)
		{
			if (expandedCandidate.Intersects(obstacle.Inflate(halfGap)))
			{
				return false;
			}
		}
		return true;
	}

	private static IEnumerable<GridOffset> EnumerateRing(
		int ring,
		double alongStep,
		double acrossStep)
	{
		if (ring == 0)
		{
			yield return new GridOffset(0, 0, 0.0);
			yield break;
		}

		List<GridOffset> offsets = new List<GridOffset>();
		for (int along = -ring; along <= ring; along++)
		{
			for (int across = -ring; across <= ring; across++)
			{
				if (Math.Max(Math.Abs(along), Math.Abs(across)) != ring)
				{
					continue;
				}
				double dx = along * alongStep;
				double dy = across * acrossStep;
				offsets.Add(new GridOffset(
					along,
					across,
					dx * dx + dy * dy));
			}
		}

		foreach (GridOffset offset in offsets
			.OrderBy(item => item.DistanceSquared)
			.ThenBy(item => Math.Abs(item.Along))
			.ThenByDescending(item => item.Across)
			.ThenByDescending(item => item.Along))
		{
			yield return offset;
		}
	}

	private static double CalculateSeparationStep(
		AutoTagPipeRectangle bounds,
		AutoTagPipePoint direction,
		double gap)
	{
		double step = double.PositiveInfinity;
		double absoluteX = Math.Abs(direction.X);
		double absoluteY = Math.Abs(direction.Y);
		if (absoluteX > Epsilon)
		{
			step = Math.Min(step, (bounds.Width + gap) / absoluteX);
		}
		if (absoluteY > Epsilon)
		{
			step = Math.Min(step, (bounds.Height + gap) / absoluteY);
		}
		return double.IsInfinity(step)
			? Math.Max(bounds.Width, bounds.Height) + gap
			: step;
	}

	private static AutoTagPipePoint Normalize(AutoTagPipePoint value)
	{
		double length = value.Length;
		return new AutoTagPipePoint(value.X / length, value.Y / length);
	}

	private struct GridOffset
	{
		internal GridOffset(int along, int across, double distanceSquared)
		{
			Along = along;
			Across = across;
			DistanceSquared = distanceSquared;
		}

		internal int Along;

		internal int Across;

		internal double DistanceSquared;
	}
}
