using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal readonly struct FitSpaceScalarInput
{
	internal FitSpaceScalarInput(double referenceSize, double includedAngleRadians)
	{
		ReferenceSize = referenceSize;
		IncludedAngleRadians = includedAngleRadians;
	}

	internal double ReferenceSize { get; }
	internal double IncludedAngleRadians { get; }
}

internal readonly struct FitSpaceScalarTarget
{
	internal FitSpaceScalarTarget(double rawMiddleLength, double standoff)
	{
		RawMiddleLength = rawMiddleLength;
		Standoff = standoff;
	}

	internal double RawMiddleLength { get; }
	internal double Standoff { get; }
}

internal sealed class FitSpaceOwnershipSet
{
	internal FitSpaceOwnershipSet(long key, IReadOnlyList<long> ids)
	{
		Key = key;
		Ids = ids ?? Array.Empty<long>();
	}

	internal long Key { get; }
	internal IReadOnlyList<long> Ids { get; }
}

internal readonly struct FitSpaceOwnershipOverlap
{
	internal FitSpaceOwnershipOverlap(long firstKey, long secondKey)
	{
		FirstKey = firstKey;
		SecondKey = secondKey;
	}

	internal long FirstKey { get; }
	internal long SecondKey { get; }
}

internal static class FitSpaceOwnershipPolicy
{
	internal static void Classify(
		IReadOnlyList<FitSpaceOwnershipSet> sets,
		out IReadOnlyList<long> duplicateKeys,
		out IReadOnlyList<FitSpaceOwnershipOverlap> overlaps)
	{
		List<long> duplicates = new List<long>();
		List<FitSpaceOwnershipOverlap> overlapRows = new List<FitSpaceOwnershipOverlap>();
		List<(long key, HashSet<long> ids)> accepted = new List<(long key, HashSet<long> ids)>();
		foreach (FitSpaceOwnershipSet set in sets ?? Array.Empty<FitSpaceOwnershipSet>())
		{
			HashSet<long> current = new HashSet<long>(set.Ids ?? Array.Empty<long>());
			if (accepted.Any(item => item.ids.SetEquals(current)))
			{
				duplicates.Add(set.Key);
				continue;
			}

			foreach ((long key, HashSet<long> ids) previous in accepted)
			{
				if (previous.ids.Overlaps(current))
				{
					overlapRows.Add(new FitSpaceOwnershipOverlap(previous.key, set.Key));
				}
			}
			accepted.Add((set.Key, current));
		}

		duplicateKeys = duplicates;
		overlaps = overlapRows;
	}
}
