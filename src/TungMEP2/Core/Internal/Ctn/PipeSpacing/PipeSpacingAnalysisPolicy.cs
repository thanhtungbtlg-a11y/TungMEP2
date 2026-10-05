using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal readonly struct PipeSpacingOwnershipSet
{
	internal PipeSpacingOwnershipSet(long key, IReadOnlyCollection<long> ownerIds)
	{
		Key = key;
		OwnerIds = ownerIds ?? Array.Empty<long>();
	}

	internal long Key { get; }
	internal IReadOnlyCollection<long> OwnerIds { get; }
}

internal static class PipeSpacingAnalysisPolicy
{
	internal static bool TryFindOwnershipOverlap(
		IReadOnlyList<PipeSpacingOwnershipSet> sets,
		out long firstKey,
		out long secondKey)
	{
		firstKey = 0;
		secondKey = 0;
		if (sets == null)
		{
			return false;
		}
		for (int first = 0; first < sets.Count; first++)
		{
			HashSet<long> firstIds = sets[first].OwnerIds.ToHashSet();
			for (int second = first + 1; second < sets.Count; second++)
			{
				if (!sets[second].OwnerIds.Any(firstIds.Contains))
				{
					continue;
				}
				firstKey = sets[first].Key;
				secondKey = sets[second].Key;
				return true;
			}
		}
		return false;
	}
}

