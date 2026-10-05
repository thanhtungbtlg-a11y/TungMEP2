using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal sealed class PipeSpacingElementSnapshot
{
	internal PipeSpacingElementSnapshot(Element element)
	{
		ElementId = element?.Id ?? throw new ArgumentNullException(nameof(element));
		UniqueId = element.UniqueId ?? string.Empty;
	}

	internal ElementId ElementId { get; }
	internal string UniqueId { get; }
}

internal sealed class PipeSpacingPlanEntry
{
	internal PipeSpacingPlanEntry(
		PipeSpacingCluster cluster,
		string seedPipeUniqueId,
		double nominalDiameter,
		double effectiveDiameter,
		double originalCoordinate,
		double targetCoordinate,
		XYZ moveVector,
		bool isAnchor,
		XYZ originalStart,
		XYZ originalEnd,
		IReadOnlyList<PipeSpacingElementSnapshot> snapshots,
		IReadOnlyList<string> warnings)
	{
		Cluster = cluster;
		SeedPipeUniqueId = seedPipeUniqueId ?? string.Empty;
		NominalDiameter = nominalDiameter;
		EffectiveDiameter = effectiveDiameter;
		OriginalCoordinate = originalCoordinate;
		TargetCoordinate = targetCoordinate;
		MoveVector = moveVector;
		IsAnchor = isAnchor;
		OriginalStart = originalStart;
		OriginalEnd = originalEnd;
		Snapshots = snapshots ?? Array.Empty<PipeSpacingElementSnapshot>();
		Warnings = warnings ?? Array.Empty<string>();
	}

	internal PipeSpacingCluster Cluster { get; }
	internal string SeedPipeUniqueId { get; }
	internal double NominalDiameter { get; }
	internal double EffectiveDiameter { get; }
	internal double OriginalCoordinate { get; }
	internal double TargetCoordinate { get; }
	internal XYZ MoveVector { get; }
	internal bool IsAnchor { get; }
	internal XYZ OriginalStart { get; }
	internal XYZ OriginalEnd { get; }
	internal IReadOnlyList<PipeSpacingElementSnapshot> Snapshots { get; }
	internal IReadOnlyList<string> Warnings { get; }
}

internal sealed class PipeSpacingAnalysisRow
{
	internal long Key { get; set; }
	internal string PipeId { get; set; } = string.Empty;
	internal double NominalDiameterMm { get; set; }
	internal double EffectiveDiameterMm { get; set; }
	internal double CurrentCoordinateMm { get; set; }
	internal double TargetCoordinateMm { get; set; }
	internal double MoveMm { get; set; }
	internal bool IsAnchor { get; set; }
	internal string Status { get; set; } = string.Empty;
	internal string Warning { get; set; } = string.Empty;
}

internal sealed class PipeSpacingAnalysisReport
{
	internal int SelectedPipeCount { get; set; }
	internal List<PipeSpacingAnalysisRow> Rows { get; } = new List<PipeSpacingAnalysisRow>();
	internal List<string> Warnings { get; } = new List<string>();
}

internal sealed class PipeSpacingPlan
{
	internal PipeSpacingPlan(
		ElementId activeViewId,
		XYZ runAxis,
		XYZ spacingAxis,
		PipeSpacingSettings settings,
		IReadOnlyList<PipeSpacingPlanEntry> entries,
		double minimumClearGap,
		string anchorReason)
	{
		ActiveViewId = activeViewId;
		RunAxis = runAxis;
		SpacingAxis = spacingAxis;
		Settings = settings;
		Entries = entries;
		MinimumClearGap = minimumClearGap;
		AnchorReason = anchorReason ?? string.Empty;
	}

	internal ElementId ActiveViewId { get; }
	internal XYZ RunAxis { get; }
	internal XYZ SpacingAxis { get; }
	internal PipeSpacingSettings Settings { get; }
	internal IReadOnlyList<PipeSpacingPlanEntry> Entries { get; }
	internal double MinimumClearGap { get; }
	internal string AnchorReason { get; }
}

