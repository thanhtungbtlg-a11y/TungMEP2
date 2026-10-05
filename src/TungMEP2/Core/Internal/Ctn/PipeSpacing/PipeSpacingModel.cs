using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal enum PipeSpacingMode
{
	EqualClearGap,
	EqualCenterSpacing,
	DnRuleTable
}

internal readonly struct PipeSpacingScalarInput
{
	internal PipeSpacingScalarInput(
		long key,
		double coordinate,
		double effectiveDiameter,
		double nominalDiameter)
	{
		Key = key;
		Coordinate = coordinate;
		EffectiveDiameter = effectiveDiameter;
		NominalDiameter = nominalDiameter;
	}

	internal long Key { get; }
	internal double Coordinate { get; }
	internal double EffectiveDiameter { get; }
	internal double NominalDiameter { get; }
}

internal readonly struct PipeSpacingDnRule
{
	internal PipeSpacingDnRule(double maxNominalDiameter, double clearGap)
	{
		MaxNominalDiameter = maxNominalDiameter;
		ClearGap = clearGap;
	}

	internal double MaxNominalDiameter { get; }
	internal double ClearGap { get; }
}

internal readonly struct PipeSpacingScalarTarget
{
	internal PipeSpacingScalarTarget(
		long key,
		double originalCoordinate,
		double targetCoordinate,
		bool isAnchor)
	{
		Key = key;
		OriginalCoordinate = originalCoordinate;
		TargetCoordinate = targetCoordinate;
		Delta = targetCoordinate - originalCoordinate;
		IsAnchor = isAnchor;
	}

	internal long Key { get; }
	internal double OriginalCoordinate { get; }
	internal double TargetCoordinate { get; }
	internal double Delta { get; }
	internal bool IsAnchor { get; }
}

internal sealed class PipeSpacingScalarPlan
{
	internal PipeSpacingScalarPlan(
		int anchorIndex,
		IReadOnlyList<PipeSpacingScalarTarget> targets,
		IReadOnlyList<double> pairClearGaps,
		double minimumClearGap)
	{
		AnchorIndex = anchorIndex;
		Targets = targets ?? throw new ArgumentNullException(nameof(targets));
		PairClearGaps = pairClearGaps ?? throw new ArgumentNullException(nameof(pairClearGaps));
		MinimumClearGap = minimumClearGap;
	}

	internal int AnchorIndex { get; }
	internal IReadOnlyList<PipeSpacingScalarTarget> Targets { get; }
	internal IReadOnlyList<double> PairClearGaps { get; }
	internal double MinimumClearGap { get; }
}

internal sealed class PipeSpacingSettings
{
	internal const int CurrentVersion = 1;
	internal const double DefaultDnRuleMaxMm = 1000.0;
	internal const double DefaultClearGapMm = 75.0;

	internal int Version { get; set; } = 1;
	internal PipeSpacingMode Mode { get; set; } = PipeSpacingMode.EqualClearGap;
	internal double ClearGapMm { get; set; } = DefaultClearGapMm;
	internal double CenterSpacingMm { get; set; } = 200.0;
	internal double FallbackClearGapMm { get; set; } = DefaultClearGapMm;
	internal bool IncludeInsulation { get; set; }
	internal bool IncludeFittingEnvelope { get; set; }
	internal double ParallelAngleToleranceDegrees { get; set; } = 2.0;
	internal bool PreviewBeforeApply { get; set; } = true;
	internal IReadOnlyList<PipeSpacingDnRule> DnRules { get; set; } = Array.Empty<PipeSpacingDnRule>();

	internal static PipeSpacingSettings CreateDefault()
	{
		return new PipeSpacingSettings
		{
			DnRules = CreateDefaultDnRules()
		};
	}

	internal PipeSpacingSettings Normalize()
	{
		PipeSpacingSettings defaults = CreateDefault();
		List<PipeSpacingDnRule> normalizedRules = (DnRules ?? Array.Empty<PipeSpacingDnRule>())
			.Where(rule => IsFinite(rule.MaxNominalDiameter) &&
			               IsFinite(rule.ClearGap) &&
			               rule.MaxNominalDiameter > 0.0 &&
			               rule.ClearGap >= 0.0)
			.GroupBy(rule => rule.MaxNominalDiameter)
			.Select(group => group.Last())
			.OrderBy(rule => rule.MaxNominalDiameter)
			.ToList();
		if (normalizedRules.Count == 0)
		{
			normalizedRules = defaults.DnRules.ToList();
		}
		return new PipeSpacingSettings
		{
			Version = CurrentVersion,
			Mode = Enum.IsDefined(typeof(PipeSpacingMode), Mode)
				? Mode
				: defaults.Mode,
			ClearGapMm = IsFinite(ClearGapMm) && ClearGapMm >= 0.0
				? ClearGapMm
				: defaults.ClearGapMm,
			CenterSpacingMm = IsFinite(CenterSpacingMm) && CenterSpacingMm > 0.0
				? CenterSpacingMm
				: defaults.CenterSpacingMm,
			FallbackClearGapMm = IsFinite(FallbackClearGapMm) && FallbackClearGapMm >= 0.0
				? FallbackClearGapMm
				: defaults.FallbackClearGapMm,
			IncludeInsulation = IncludeInsulation,
			IncludeFittingEnvelope = IncludeFittingEnvelope,
			ParallelAngleToleranceDegrees = IsFinite(ParallelAngleToleranceDegrees)
				? Math.Max(0.1, Math.Min(10.0, ParallelAngleToleranceDegrees))
				: defaults.ParallelAngleToleranceDegrees,
			PreviewBeforeApply = PreviewBeforeApply,
			DnRules = normalizedRules
		};
	}

	private static IReadOnlyList<PipeSpacingDnRule> CreateDefaultDnRules()
	{
		return new[]
		{
			new PipeSpacingDnRule(DefaultDnRuleMaxMm, DefaultClearGapMm)
		};
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
