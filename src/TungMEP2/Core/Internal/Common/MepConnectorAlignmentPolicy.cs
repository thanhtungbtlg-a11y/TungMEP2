using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Common;

internal readonly record struct MepConnectorPairCandidate(
	int FixedIndex,
	int MovingIndex,
	bool FixedIsConnected,
	bool MovingIsConnected,
	bool IsDomainCompatible,
	bool IsDirectionCompatible,
	double Distance);

internal readonly record struct MepConnectorPairDecision(
	int FixedIndex,
	int MovingIndex,
	bool ShouldConnect,
	bool UsesConnectedFallback);

internal static class MepConnectorAlignmentPolicy
{
	internal static bool ShouldUseConnectedCluster(
		MepConnectorAlignmentMode mode)
	{
		return mode == MepConnectorAlignmentMode.MoveConnectAlign ||
		       mode == MepConnectorAlignmentMode.AlignIn3D;
	}

	internal static bool ShouldCreateConnection(
		MepConnectorAlignmentMode mode,
		MepConnectorPairDecision decision)
	{
		return mode != MepConnectorAlignmentMode.AlignIn3D &&
		       decision.ShouldConnect;
	}

	internal static bool TryChoosePair(
		IReadOnlyList<MepConnectorPairCandidate> candidates,
		bool requireDirectionCompatibility,
		bool allowConnectedFallback,
		out MepConnectorPairDecision decision)
	{
		decision = default;
		if (candidates == null || candidates.Count == 0)
		{
			return false;
		}

		List<MepConnectorPairCandidate> eligible = candidates
			.Where(candidate => candidate.IsDomainCompatible)
			.Where(candidate =>
				!requireDirectionCompatibility ||
				candidate.IsDirectionCompatible)
			.ToList();

		MepConnectorPairCandidate? openChoice = eligible
			.Where(candidate =>
				!candidate.FixedIsConnected &&
				!candidate.MovingIsConnected)
			.OrderBy(candidate => candidate.Distance)
			.Select(candidate => (MepConnectorPairCandidate?)candidate)
			.FirstOrDefault();
		if (openChoice.HasValue)
		{
			MepConnectorPairCandidate candidate = openChoice.Value;
			decision = new MepConnectorPairDecision(
				candidate.FixedIndex,
				candidate.MovingIndex,
				ShouldConnect: true,
				UsesConnectedFallback: false);
			return true;
		}

		if (!allowConnectedFallback)
		{
			return false;
		}

		MepConnectorPairCandidate? fallback = eligible
			.OrderBy(candidate => candidate.Distance)
			.Select(candidate => (MepConnectorPairCandidate?)candidate)
			.FirstOrDefault();
		if (!fallback.HasValue)
		{
			return false;
		}

		MepConnectorPairCandidate fallbackCandidate = fallback.Value;
		decision = new MepConnectorPairDecision(
			fallbackCandidate.FixedIndex,
			fallbackCandidate.MovingIndex,
			ShouldConnect: false,
			UsesConnectedFallback: true);
		return true;
	}
}
