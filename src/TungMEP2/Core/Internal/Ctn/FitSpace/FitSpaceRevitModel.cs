using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal sealed class FitSpaceConnectorKey
{
	internal FitSpaceConnectorKey(
		ElementId ownerId,
		string ownerUniqueId,
		int connectorId,
		XYZ originalOrigin,
		XYZ originalBasisX = null,
		XYZ originalBasisY = null,
		XYZ originalBasisZ = null,
		ElementId originalSystemId = null,
		ConnectorProfileType originalShape = ConnectorProfileType.Invalid,
		double originalRadius = 0.0,
		double originalWidth = 0.0,
		double originalHeight = 0.0)
	{
		OwnerId = ownerId;
		OwnerUniqueId = ownerUniqueId ?? string.Empty;
		ConnectorId = connectorId;
		OriginalOrigin = originalOrigin;
		OriginalBasisX = originalBasisX;
		OriginalBasisY = originalBasisY;
		OriginalBasisZ = originalBasisZ;
		OriginalSystemId = originalSystemId ?? ElementId.InvalidElementId;
		OriginalShape = originalShape;
		OriginalRadius = originalRadius;
		OriginalWidth = originalWidth;
		OriginalHeight = originalHeight;
	}

	internal ElementId OwnerId { get; }
	internal string OwnerUniqueId { get; }
	internal int ConnectorId { get; }
	internal XYZ OriginalOrigin { get; }
	internal XYZ OriginalBasisX { get; }
	internal XYZ OriginalBasisY { get; }
	internal XYZ OriginalBasisZ { get; }
	internal ElementId OriginalSystemId { get; }
	internal ConnectorProfileType OriginalShape { get; }
	internal double OriginalRadius { get; }
	internal double OriginalWidth { get; }
	internal double OriginalHeight { get; }
}

internal sealed class FitSpaceConnectorBinding
{
	internal FitSpaceConnectorBinding(FitSpaceConnectorKey first, FitSpaceConnectorKey second)
	{
		First = first;
		Second = second;
	}

	internal FitSpaceConnectorKey First { get; }
	internal FitSpaceConnectorKey Second { get; }
}

internal sealed class FitSpaceElementSnapshot
{
	internal FitSpaceElementSnapshot(Element element)
	{
		Id = element.Id;
		UniqueId = element.UniqueId;
		TypeId = element.GetTypeId();
		CategoryId = element.Category?.Id ?? ElementId.InvalidElementId;
		IsPinned = element.Pinned;
		GroupId = element.GroupId;
		DesignOptionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
		Diameter = ReadDouble(element, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		Slope = ReadDouble(element, BuiltInParameter.RBS_PIPE_SLOPE);
		SystemTypeId = ReadElementId(element, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
	}

	internal ElementId Id { get; }
	internal string UniqueId { get; }
	internal ElementId TypeId { get; }
	internal ElementId CategoryId { get; }
	internal bool IsPinned { get; }
	internal ElementId GroupId { get; }
	internal ElementId DesignOptionId { get; }
	internal double? Diameter { get; }
	internal double? Slope { get; }
	internal ElementId SystemTypeId { get; }

	private static double? ReadDouble(Element element, BuiltInParameter parameter)
	{
		Parameter value = element.get_Parameter(parameter);
		return value != null && value.StorageType == StorageType.Double ? value.AsDouble() : null;
	}

	private static ElementId ReadElementId(Element element, BuiltInParameter parameter)
	{
		Parameter value = element.get_Parameter(parameter);
		return value != null && value.StorageType == StorageType.ElementId
			? value.AsElementId()
			: ElementId.InvalidElementId;
	}
}

internal sealed class FitSpaceFarEndSnapshot
{
	internal FitSpaceFarEndSnapshot(
		ElementId outerPipeId,
		FitSpaceConnectorKey pipeConnector,
		XYZ point,
		FitSpaceConnectorBinding externalBinding)
	{
		OuterPipeId = outerPipeId;
		PipeConnector = pipeConnector;
		Point = point;
		ExternalBinding = externalBinding;
	}

	internal ElementId OuterPipeId { get; }
	internal FitSpaceConnectorKey PipeConnector { get; }
	internal XYZ Point { get; }
	internal FitSpaceConnectorBinding ExternalBinding { get; }
	internal bool WasConnected => ExternalBinding != null;
}

internal sealed class FitSpaceExternalElementSnapshot
{
	internal FitSpaceExternalElementSnapshot(
		FitSpaceElementSnapshot element,
		IReadOnlyList<FitSpaceConnectorKey> connectors,
		string locationKind,
		XYZ locationStart,
		XYZ locationMiddle,
		XYZ locationEnd,
		double? locationRotation,
		XYZ transformOrigin,
		XYZ transformBasisX,
		XYZ transformBasisY,
		XYZ transformBasisZ)
	{
		Element = element;
		Connectors = connectors ?? Array.Empty<FitSpaceConnectorKey>();
		LocationKind = locationKind ?? string.Empty;
		LocationStart = locationStart;
		LocationMiddle = locationMiddle;
		LocationEnd = locationEnd;
		LocationRotation = locationRotation;
		TransformOrigin = transformOrigin;
		TransformBasisX = transformBasisX;
		TransformBasisY = transformBasisY;
		TransformBasisZ = transformBasisZ;
	}

	internal FitSpaceElementSnapshot Element { get; }
	internal IReadOnlyList<FitSpaceConnectorKey> Connectors { get; }
	internal string LocationKind { get; }
	internal XYZ LocationStart { get; }
	internal XYZ LocationMiddle { get; }
	internal XYZ LocationEnd { get; }
	internal double? LocationRotation { get; }
	internal XYZ TransformOrigin { get; }
	internal XYZ TransformBasisX { get; }
	internal XYZ TransformBasisY { get; }
	internal XYZ TransformBasisZ { get; }
}

internal sealed class FitSpaceCluster
{
	internal FitSpaceCluster(
		long key,
		ElementId middlePipeId,
		ElementId elbow1Id,
		ElementId elbow2Id,
		ElementId outerPipe1Id,
		ElementId outerPipe2Id,
		IReadOnlyList<FitSpaceConnectorBinding> localBindings,
		FitSpaceFarEndSnapshot farEnd1,
		FitSpaceFarEndSnapshot farEnd2,
		IReadOnlyList<FitSpaceElementSnapshot> elementSnapshots,
		IReadOnlyList<FitSpaceExternalElementSnapshot> externalElementSnapshots = null)
	{
		Key = key;
		MiddlePipeId = middlePipeId;
		Elbow1Id = elbow1Id;
		Elbow2Id = elbow2Id;
		OuterPipe1Id = outerPipe1Id;
		OuterPipe2Id = outerPipe2Id;
		LocalBindings = localBindings;
		FarEnd1 = farEnd1;
		FarEnd2 = farEnd2;
		ElementSnapshots = elementSnapshots;
		ExternalElementSnapshots = externalElementSnapshots ?? Array.Empty<FitSpaceExternalElementSnapshot>();
		OwnedElementIds = new[] { middlePipeId, elbow1Id, elbow2Id, outerPipe1Id, outerPipe2Id };
	}

	internal long Key { get; }
	internal ElementId MiddlePipeId { get; }
	internal ElementId Elbow1Id { get; }
	internal ElementId Elbow2Id { get; }
	internal ElementId OuterPipe1Id { get; }
	internal ElementId OuterPipe2Id { get; }
	internal IReadOnlyList<FitSpaceConnectorBinding> LocalBindings { get; }
	internal FitSpaceFarEndSnapshot FarEnd1 { get; }
	internal FitSpaceFarEndSnapshot FarEnd2 { get; }
	internal IReadOnlyList<FitSpaceElementSnapshot> ElementSnapshots { get; }
	internal IReadOnlyList<FitSpaceExternalElementSnapshot> ExternalElementSnapshots { get; }
	internal IReadOnlyList<ElementId> OwnedElementIds { get; }

	internal FitSpaceCluster WithExternalElementSnapshots(IReadOnlyList<FitSpaceExternalElementSnapshot> snapshots)
	{
		return new FitSpaceCluster(
			Key,
			MiddlePipeId,
			Elbow1Id,
			Elbow2Id,
			OuterPipe1Id,
			OuterPipe2Id,
			LocalBindings,
			FarEnd1,
			FarEnd2,
			ElementSnapshots,
			snapshots);
	}
}

internal sealed class FitSpaceTargetGeometry
{
	internal FitSpaceTargetGeometry(
		XYZ currentApex1,
		XYZ currentApex2,
		XYZ targetApex1,
		XYZ targetApex2,
		XYZ elbow1Translation,
		XYZ elbow2Translation,
		Line outerPipe1Line,
		Line middlePipeLine,
		Line outerPipe2Line,
		double referenceSize,
		double includedAngle,
		bool isAlreadyNormalized)
	{
		CurrentApex1 = currentApex1;
		CurrentApex2 = currentApex2;
		TargetApex1 = targetApex1;
		TargetApex2 = targetApex2;
		Elbow1Translation = elbow1Translation;
		Elbow2Translation = elbow2Translation;
		OuterPipe1Line = outerPipe1Line;
		MiddlePipeLine = middlePipeLine;
		OuterPipe2Line = outerPipe2Line;
		ReferenceSize = referenceSize;
		IncludedAngle = includedAngle;
		IsAlreadyNormalized = isAlreadyNormalized;
	}

	internal XYZ CurrentApex1 { get; }
	internal XYZ CurrentApex2 { get; }
	internal XYZ TargetApex1 { get; }
	internal XYZ TargetApex2 { get; }
	internal XYZ Elbow1Translation { get; }
	internal XYZ Elbow2Translation { get; }
	internal Line OuterPipe1Line { get; }
	internal Line MiddlePipeLine { get; }
	internal Line OuterPipe2Line { get; }
	internal double ReferenceSize { get; }
	internal double IncludedAngle { get; }
	internal bool IsAlreadyNormalized { get; }
}

internal sealed class FitSpacePlanEntry
{
	internal FitSpacePlanEntry(FitSpaceCluster cluster, FitSpaceTargetGeometry target)
	{
		Cluster = cluster;
		Target = target;
	}

	internal FitSpaceCluster Cluster { get; }
	internal FitSpaceTargetGeometry Target { get; }
}

internal sealed class FitSpacePlan
{
	internal FitSpacePlan(IReadOnlyList<FitSpacePlanEntry> entries)
	{
		Entries = entries ?? Array.Empty<FitSpacePlanEntry>();
	}

	internal IReadOnlyList<FitSpacePlanEntry> Entries { get; }
}

internal sealed class FitSpaceReportRow
{
	internal FitSpaceReportRow(long key, string reason)
	{
		Key = key;
		Reason = reason ?? string.Empty;
	}

	internal long Key { get; }
	internal string Reason { get; }
}

internal sealed class FitSpaceCollectionReport
{
	internal FitSpaceCollectionReport(IReadOnlyList<FitSpaceReportRow> skipped)
	{
		Skipped = skipped ?? Array.Empty<FitSpaceReportRow>();
	}

	internal IReadOnlyList<FitSpaceReportRow> Skipped { get; }
}

internal sealed class FitSpaceAnalysisReport
{
	internal FitSpaceAnalysisReport(
		IReadOnlyList<FitSpaceReportRow> excluded,
		int alreadyNormalizedCount)
	{
		Excluded = excluded ?? Array.Empty<FitSpaceReportRow>();
		AlreadyNormalizedCount = alreadyNormalizedCount;
	}

	internal IReadOnlyList<FitSpaceReportRow> Excluded { get; }
	internal int AlreadyNormalizedCount { get; }
}

internal sealed class FitSpaceExecutionReport
{
	internal FitSpaceExecutionReport(
		int adjustedCount,
		int alreadyNormalizedCount,
		int skippedCount,
		int rolledBackCount,
		IReadOnlyList<FitSpaceReportRow> skipped,
		IReadOnlyList<FitSpaceReportRow> rolledBack)
	{
		AdjustedCount = adjustedCount;
		AlreadyNormalizedCount = alreadyNormalizedCount;
		SkippedCount = skippedCount;
		RolledBackCount = rolledBackCount;
		Skipped = skipped ?? Array.Empty<FitSpaceReportRow>();
		RolledBack = rolledBack ?? Array.Empty<FitSpaceReportRow>();
	}

	internal int AdjustedCount { get; }
	internal int AlreadyNormalizedCount { get; }
	internal int SkippedCount { get; }
	internal int RolledBackCount { get; }
	internal IReadOnlyList<FitSpaceReportRow> Skipped { get; }
	internal IReadOnlyList<FitSpaceReportRow> RolledBack { get; }
}
