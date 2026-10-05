using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_CopyGridViewFloor : SecuredExternalCommand
{
	private sealed class SourceGridState
	{
		public IReadOnlyList<Curve> Curves { get; }

		public bool End0BubbleVisible { get; }

		public bool End1BubbleVisible { get; }

		public SourceGridState(IReadOnlyList<Curve> curves, bool end0BubbleVisible, bool end1BubbleVisible)
		{
			Curves = curves;
			End0BubbleVisible = end0BubbleVisible;
			End1BubbleVisible = end1BubbleVisible;
		}
	}

	private sealed class DimensionSourceState
	{
		public ElementId SourceDimensionId { get; }

		public Line Line { get; }

		public IReadOnlyList<long> GridReferenceIds { get; }

		public ElementId DimensionTypeId { get; }

		public DimensionTextOverride TextOverride { get; }

		public IReadOnlyList<DimensionSegmentOverride> SegmentOverrides { get; }

		public DimensionSourceState(ElementId sourceDimensionId, Line line, IReadOnlyList<long> gridReferenceIds, ElementId dimensionTypeId, DimensionTextOverride textOverride, IReadOnlyList<DimensionSegmentOverride> segmentOverrides)
		{
			SourceDimensionId = sourceDimensionId;
			Line = line;
			GridReferenceIds = gridReferenceIds;
			DimensionTypeId = dimensionTypeId;
			TextOverride = textOverride;
			SegmentOverrides = segmentOverrides;
		}
	}

	private sealed class DimensionTextOverride
	{
		public string Prefix { get; }

		public string Suffix { get; }

		public string Above { get; }

		public string Below { get; }

		public string ValueOverride { get; }

		public DimensionTextOverride(string prefix, string suffix, string above, string below, string valueOverride)
		{
			Prefix = prefix;
			Suffix = suffix;
			Above = above;
			Below = below;
			ValueOverride = valueOverride;
		}
	}

	private sealed class DimensionSegmentOverride
	{
		public int Index { get; }

		public DimensionTextOverride TextOverride { get; }

		public DimensionSegmentOverride(int index, DimensionTextOverride textOverride)
		{
			Index = index;
			TextOverride = textOverride;
		}
	}

	private sealed class CopyDimensionToTargetResult
	{
		public int Copied { get; set; }

		public int PartialCopied { get; set; }

		public int Skipped { get; set; }

		public int Failed { get; set; }

		public int GraphicsCopiedVerified { get; set; }

		public int GraphicsNoSourceOverride { get; set; }

		public int GraphicsMismatchedAfterSet { get; set; }

		public int GraphicsFailed { get; set; }

		public List<string> IssueDetails { get; } = new List<string>();
	}

	private sealed class TargetCopyStats
	{
		public int Copied { get; set; }

		public int Skipped { get; set; }

		public int Need2DConvert { get; set; }

		public int ConvertedTo2D { get; set; }

		public int Convert2DFailed { get; set; }

		public int PartialCopied { get; set; }

		public int CurveDirectSetOk { get; set; }

		public int CurveNormalizedSetOk { get; set; }

		public int CurveFailed { get; set; }

		public int BubbleFailed { get; set; }
	}

	private enum DimensionGraphicsApplyResult
	{
		Failed,
		CopiedVerified,
		NoSourceOverride,
		MismatchedAfterSet
	}

	private enum CurveApplyResult
	{
		Failed,
		SuccessDirect,
		SuccessNormalized
	}

	private const string DialogTitle = "CopyGridViewFloor";

	protected override void ExecuteCore()
	{
		try
		{
			UI_CopyGridViewFloorWindow window = new UI_CopyGridViewFloorWindow(base.UiDocument);
			if (window.ShowDialog() != true)
			{
				return;
			}
			if (!TryResolveSelectedViews(window.SelectedSourceViewId, window.SelectedTargetViewIds, out ViewPlan sourceView, out List<ViewPlan> targetViews, out string selectionError))
			{
				TaskDialog.Show("CopyGridViewFloor", selectionError);
				return;
			}
			List<Grid> sourceGrids = CollectVisibleGrids(sourceView);
			if (sourceGrids.Count == 0)
			{
				TaskDialog.Show("CopyGridViewFloor", "View gốc không có grid hiển thị.");
				return;
			}
			if (TryFindSourceModelExtentGrids(sourceView, sourceGrids, out List<string> modelExtentGridNames))
			{
				string message = "View gốc đang có Grid 3D (Model extent). Hãy chuyển các Grid sau về 2D rồi chạy lại:\n\n" + string.Join("\n", modelExtentGridNames);
				TaskDialog.Show("CopyGridViewFloor", message);
				return;
			}
			Dictionary<long, SourceGridState> sourceStates = BuildSourceGridStates(sourceView, sourceGrids);
			if (sourceStates.Count == 0)
			{
				TaskDialog.Show("CopyGridViewFloor", "Không đọc được dữ liệu grid từ view gốc.");
				return;
			}
			int dimSourceFound;
			int dimEligibleGridOnly;
			List<DimensionSourceState> sourceDimensionStates = CollectSourceGridOnlyDimensions(sourceView, sourceStates.Keys, out dimSourceFound, out dimEligibleGridOnly);
			List<string> reportLines = new List<string>();
			int totalCopied = 0;
			int totalSkipped = 0;
			int totalSourceStates = sourceStates.Count;
			int totalMatchedById = 0;
			int totalNeed2DConvert = 0;
			int totalConvertedTo2D = 0;
			int totalConvert2DFailed = 0;
			int totalPartialCopied = 0;
			int totalCurveFailed = 0;
			int totalCurveDirectSetOk = 0;
			int totalCurveNormalizedSetOk = 0;
			int totalBubbleFailed = 0;
			int totalDimCopied = 0;
			int totalDimPartialCopied = 0;
			int totalDimSkipped = 0;
			int totalDimFailed = 0;
			int totalDimGraphicsCopiedVerified = 0;
			int totalDimGraphicsNoSourceOverride = 0;
			int totalDimGraphicsMismatchedAfterSet = 0;
			int totalDimGraphicsFailed = 0;
			List<string> applyIssueDetails = new List<string>();
			using Transaction transaction = new Transaction(base.Document, "Copy Grid FloorPlan View Settings");
			transaction.Start();
			foreach (ViewPlan targetView in targetViews)
			{
				if (targetView.Id == sourceView.Id)
				{
					continue;
				}
				List<Grid> targetGrids = CollectVisibleGrids(targetView);
				TargetCopyStats targetStats = new TargetCopyStats();
				foreach (Grid targetGrid in targetGrids)
				{
					if (!sourceStates.TryGetValue(targetGrid.Id.Value, out var sourceState))
					{
						targetStats.Skipped++;
						continue;
					}
					totalMatchedById++;
					if (!TryApplyGridStateToTarget(targetView, targetGrid, sourceState, targetStats, out string issueDetail))
					{
						targetStats.Skipped++;
						if (!string.IsNullOrWhiteSpace(issueDetail))
						{
							applyIssueDetails.Add("- Target '" + targetView.Name + "' | " + issueDetail);
						}
					}
				}
				totalCopied += targetStats.Copied;
				totalSkipped += targetStats.Skipped;
				totalNeed2DConvert += targetStats.Need2DConvert;
				totalConvertedTo2D += targetStats.ConvertedTo2D;
				totalConvert2DFailed += targetStats.Convert2DFailed;
				totalPartialCopied += targetStats.PartialCopied;
				totalCurveFailed += targetStats.CurveFailed;
				totalCurveDirectSetOk += targetStats.CurveDirectSetOk;
				totalCurveNormalizedSetOk += targetStats.CurveNormalizedSetOk;
				totalBubbleFailed += targetStats.BubbleFailed;
				CopyDimensionToTargetResult dimensionResult = CopyGridDimensionsToTarget(sourceView, targetView, sourceDimensionStates, targetGrids);
				totalDimCopied += dimensionResult.Copied;
				totalDimPartialCopied += dimensionResult.PartialCopied;
				totalDimSkipped += dimensionResult.Skipped;
				totalDimFailed += dimensionResult.Failed;
				totalDimGraphicsCopiedVerified += dimensionResult.GraphicsCopiedVerified;
				totalDimGraphicsNoSourceOverride += dimensionResult.GraphicsNoSourceOverride;
				totalDimGraphicsMismatchedAfterSet += dimensionResult.GraphicsMismatchedAfterSet;
				totalDimGraphicsFailed += dimensionResult.GraphicsFailed;
				if (dimensionResult.IssueDetails.Count > 0)
				{
					applyIssueDetails.AddRange(dimensionResult.IssueDetails.Select((string issue) => "- Target '" + targetView.Name + "' | " + issue));
				}
				reportLines.Add($"- {targetView.Name}: Copied {targetStats.Copied}, " + $"PartialCopied {targetStats.PartialCopied}, " + $"Need2DConvert {targetStats.Need2DConvert}, " + $"ConvertedTo2D {targetStats.ConvertedTo2D}, " + $"Convert2DFailed {targetStats.Convert2DFailed}, " + $"CurveDirectSetOk {targetStats.CurveDirectSetOk}, " + $"CurveNormalizedSetOk {targetStats.CurveNormalizedSetOk}, " + $"CurveFailed {targetStats.CurveFailed}, " + $"BubbleFailed {targetStats.BubbleFailed}, " + $"Skipped {targetStats.Skipped}, " + $"DimCopied {dimensionResult.Copied}, " + $"DimPartialCopied {dimensionResult.PartialCopied}, " + $"DimSkipped {dimensionResult.Skipped}, " + $"DimFailed {dimensionResult.Failed}, " + $"DimGraphicsCopiedVerified {dimensionResult.GraphicsCopiedVerified}, " + $"DimGraphicsNoSourceOverride {dimensionResult.GraphicsNoSourceOverride}, " + $"DimGraphicsMismatchedAfterSet {dimensionResult.GraphicsMismatchedAfterSet}, " + $"DimGraphicsFailed {dimensionResult.GraphicsFailed}");
			}
			transaction.Commit();
			if (totalSkipped > 0 || totalPartialCopied > 0 || totalConvert2DFailed > 0 || totalCurveFailed > 0 || totalBubbleFailed > 0 || totalDimPartialCopied > 0 || totalDimSkipped > 0 || totalDimFailed > 0 || totalDimGraphicsMismatchedAfterSet > 0 || totalDimGraphicsFailed > 0 || applyIssueDetails.Count > 0)
			{
				TaskDialog.Show("CopyGridViewFloor", "Source: " + sourceView.Name + "\n" + $"Targets: {targetViews.Count}\n" + $"SourceStates: {totalSourceStates}\n" + $"MatchedById: {totalMatchedById}\n" + $"DimSourceFound: {dimSourceFound}\n" + $"DimEligibleGridOnly: {dimEligibleGridOnly}\n" + $"Total Copied: {totalCopied}\n" + $"Total PartialCopied: {totalPartialCopied}\n" + $"Total Need2DConvert: {totalNeed2DConvert}\n" + $"Total ConvertedTo2D: {totalConvertedTo2D}\n" + $"Total Convert2DFailed: {totalConvert2DFailed}\n" + $"Total CurveDirectSetOk: {totalCurveDirectSetOk}\n" + $"Total CurveNormalizedSetOk: {totalCurveNormalizedSetOk}\n" + $"Total CurveFailed: {totalCurveFailed}\n" + $"Total BubbleFailed: {totalBubbleFailed}\n" + $"Total DimCopied: {totalDimCopied}\n" + $"Total DimPartialCopied: {totalDimPartialCopied}\n" + $"Total DimSkipped: {totalDimSkipped}\n" + $"Total DimFailed: {totalDimFailed}\n" + $"Total DimGraphicsCopiedVerified: {totalDimGraphicsCopiedVerified}\n" + $"Total DimGraphicsNoSourceOverride: {totalDimGraphicsNoSourceOverride}\n" + $"Total DimGraphicsMismatchedAfterSet: {totalDimGraphicsMismatchedAfterSet}\n" + $"Total DimGraphicsFailed: {totalDimGraphicsFailed}\n" + $"Total Skipped: {totalSkipped}\n\n" + string.Join("\n", reportLines) + ((applyIssueDetails.Count == 0) ? string.Empty : ("\n\nTarget grids skipped detail:\n" + string.Join("\n", applyIssueDetails.Take(20)))));
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("CopyGridViewFloor", ex2.ToString());
		}
	}

	private bool TryResolveSelectedViews(ElementId? sourceViewId, IReadOnlyList<ElementId> targetViewIds, out ViewPlan? sourceView, out List<ViewPlan> targetViews, out string errorMessage)
	{
		sourceView = null;
		targetViews = new List<ViewPlan>();
		errorMessage = string.Empty;
		if (sourceViewId == null || sourceViewId == ElementId.InvalidElementId)
		{
			errorMessage = "Hãy chọn 1 ViewFloorPlan làm Source.";
			return false;
		}
		if (targetViewIds == null || targetViewIds.Count == 0)
		{
			errorMessage = "Hãy chọn ít nhất 1 ViewFloorPlan làm Target.";
			return false;
		}
		if (!(base.Document.GetElement(sourceViewId) is ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } sourcePlan))
		{
			errorMessage = "Source đã chọn không hợp lệ. Hãy chọn ViewFloorPlan đang mở.";
			return false;
		}
		sourceView = sourcePlan;
		HashSet<ElementId> seen = new HashSet<ElementId>();
		foreach (ElementId targetId in targetViewIds)
		{
			if (!(targetId == null) && !(targetId == ElementId.InvalidElementId) && !(targetId == sourcePlan.Id) && seen.Add(targetId) && base.Document.GetElement(targetId) is ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } targetPlan)
			{
				targetViews.Add(targetPlan);
			}
		}
		if (targetViews.Count == 0)
		{
			errorMessage = "Không có Target hợp lệ để copy.";
			return false;
		}
		return true;
	}

	private List<Grid> CollectVisibleGrids(View view)
	{
		return new FilteredElementCollector(base.Document, view.Id).OfClass(typeof(Grid)).Cast<Grid>().ToList();
	}

	private static bool TryFindSourceModelExtentGrids(View sourceView, IReadOnlyList<Grid> sourceGrids, out List<string> gridNames)
	{
		gridNames = new List<string>();
		foreach (Grid grid in sourceGrids)
		{
			DatumExtentType? end0 = TryGetDatumExtentType(grid, DatumEnds.End0, sourceView);
			DatumExtentType? end1 = TryGetDatumExtentType(grid, DatumEnds.End1, sourceView);
			if (end0 != DatumExtentType.ViewSpecific || end1 != DatumExtentType.ViewSpecific)
			{
				gridNames.Add($"- {grid.Name} (Id: {grid.Id.Value})");
			}
		}
		return gridNames.Count > 0;
	}

	private static DatumExtentType? TryGetDatumExtentType(Grid grid, DatumEnds end, View view)
	{
		try
		{
			return grid.GetDatumExtentTypeInView(end, view);
		}
		catch
		{
			return null;
		}
	}

	private static Dictionary<long, SourceGridState> BuildSourceGridStates(View sourceView, IEnumerable<Grid> sourceGrids)
	{
		Dictionary<long, SourceGridState> result = new Dictionary<long, SourceGridState>();
		foreach (Grid grid in sourceGrids)
		{
			List<Curve> sourceCurves = (from curve in grid.GetCurvesInView(DatumExtentType.ViewSpecific, sourceView)
				where curve != null
				select curve).ToList();
			if (sourceCurves.Count != 0)
			{
				bool end0Bubble = IsBubbleVisibleSafe(grid, sourceView, DatumEnds.End0);
				bool end1Bubble = IsBubbleVisibleSafe(grid, sourceView, DatumEnds.End1);
				result[grid.Id.Value] = new SourceGridState(sourceCurves, end0Bubble, end1Bubble);
			}
		}
		return result;
	}

	private List<DimensionSourceState> CollectSourceGridOnlyDimensions(View sourceView, IEnumerable<long> sourceGridIds, out int sourceFound, out int eligibleGridOnly)
	{
		HashSet<long> sourceGridIdSet = sourceGridIds.ToHashSet();
		List<Dimension> sourceDimensions = new FilteredElementCollector(base.Document, sourceView.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
		sourceFound = sourceDimensions.Count;
		eligibleGridOnly = 0;
		List<DimensionSourceState> result = new List<DimensionSourceState>();
		foreach (Dimension dimension in sourceDimensions)
		{
			if (dimension.Curve is Line line && TryGetGridReferenceIds(dimension, out List<long> gridReferenceIds) && gridReferenceIds.Count >= 2 && !gridReferenceIds.Any((long id) => !sourceGridIdSet.Contains(id)))
			{
				eligibleGridOnly++;
				result.Add(new DimensionSourceState(dimension.Id, line, gridReferenceIds, dimension.GetTypeId(), new DimensionTextOverride(dimension.Prefix, dimension.Suffix, dimension.Above, dimension.Below, dimension.ValueOverride), ReadSegmentOverrides(dimension)));
			}
		}
		return result;
	}

	private bool TryGetGridReferenceIds(Dimension dimension, out List<long> gridReferenceIds)
	{
		gridReferenceIds = new List<long>();
		ReferenceArray references;
		try
		{
			references = dimension.References;
		}
		catch
		{
			return false;
		}
		if (references == null || references.Size == 0)
		{
			return false;
		}
		foreach (Reference reference in references)
		{
			ElementId referenceElementId = reference.ElementId;
			if (referenceElementId == null || referenceElementId == ElementId.InvalidElementId)
			{
				return false;
			}
			if (!(base.Document.GetElement(referenceElementId) is Grid))
			{
				return false;
			}
			gridReferenceIds.Add(referenceElementId.Value);
		}
		return gridReferenceIds.Count >= 2;
	}

	private static List<DimensionSegmentOverride> ReadSegmentOverrides(Dimension dimension)
	{
		List<DimensionSegmentOverride> overrides = new List<DimensionSegmentOverride>();
		try
		{
			DimensionSegmentArray segments = dimension.Segments;
			if (segments == null || segments.Size == 0)
			{
				return overrides;
			}
			for (int i = 0; i < segments.Size; i++)
			{
				DimensionSegment segment = segments.get_Item(i);
				if (segment != null)
				{
					overrides.Add(new DimensionSegmentOverride(i, new DimensionTextOverride(segment.Prefix, segment.Suffix, segment.Above, segment.Below, segment.ValueOverride)));
				}
			}
		}
		catch
		{
			return overrides;
		}
		return overrides;
	}

	private CopyDimensionToTargetResult CopyGridDimensionsToTarget(View sourceView, View targetView, IReadOnlyList<DimensionSourceState> sourceDimensions, IReadOnlyList<Grid> targetGrids)
	{
		Dictionary<long, Grid> targetGridById = targetGrids.ToDictionary((Grid grid) => grid.Id.Value, (Grid grid) => grid);
		CopyDimensionToTargetResult result = new CopyDimensionToTargetResult();
		foreach (DimensionSourceState sourceDimension in sourceDimensions)
		{
			ReferenceArray references = new ReferenceArray();
			bool mappingFailed = false;
			foreach (long gridId in sourceDimension.GridReferenceIds)
			{
				if (!targetGridById.TryGetValue(gridId, out var targetGrid))
				{
					mappingFailed = true;
					result.Skipped++;
					result.IssueDetails.Add($"DIM {sourceDimension.SourceDimensionId.Value}: missing target grid {gridId}");
					break;
				}
				references.Append(new Reference(targetGrid));
			}
			if (!mappingFailed)
			{
				Line targetLine = NormalizeDimensionLineToTarget(sourceDimension.Line, sourceView, targetView);
				Dimension createdDimension;
				try
				{
					createdDimension = base.Document.Create.NewDimension(targetView, targetLine, references);
				}
				catch
				{
					result.Failed++;
					result.IssueDetails.Add($"DIM {sourceDimension.SourceDimensionId.Value}: NewDimension failed");
					continue;
				}
				bool fullApplied = true;
				if (!TryApplyDimensionType(createdDimension, sourceDimension.DimensionTypeId))
				{
					fullApplied = false;
				}
				if (!TryApplyDimensionTextOverride(createdDimension, sourceDimension.TextOverride))
				{
					fullApplied = false;
				}
				if (!TryApplyDimensionSegmentOverrides(createdDimension, sourceDimension.SegmentOverrides))
				{
					fullApplied = false;
				}
				switch (TryApplyDimensionGraphicsOverrides(sourceView, targetView, sourceDimension.SourceDimensionId, createdDimension.Id))
				{
				case DimensionGraphicsApplyResult.CopiedVerified:
					result.GraphicsCopiedVerified++;
					break;
				case DimensionGraphicsApplyResult.NoSourceOverride:
					result.GraphicsNoSourceOverride++;
					break;
				case DimensionGraphicsApplyResult.MismatchedAfterSet:
					result.GraphicsMismatchedAfterSet++;
					fullApplied = false;
					break;
				default:
					result.GraphicsFailed++;
					fullApplied = false;
					break;
				}
				if (fullApplied)
				{
					result.Copied++;
				}
				else
				{
					result.PartialCopied++;
				}
			}
		}
		return result;
	}

	private static DimensionGraphicsApplyResult TryApplyDimensionGraphicsOverrides(View sourceView, View targetView, ElementId sourceDimensionId, ElementId targetDimensionId)
	{
		try
		{
			OverrideGraphicSettings sourceOverride = sourceView.GetElementOverrides(sourceDimensionId);
			targetView.SetElementOverrides(targetDimensionId, sourceOverride);
			return DimensionGraphicsApplyResult.CopiedVerified;
		}
		catch
		{
			return DimensionGraphicsApplyResult.Failed;
		}
	}

	private static Line NormalizeDimensionLineToTarget(Line sourceLine, View sourceView, View targetView)
	{
		double sourceElevation = GetViewElevation(sourceView);
		double targetElevation = GetViewElevation(targetView);
		double deltaZ = targetElevation - sourceElevation;
		Transform translation = Transform.CreateTranslation(new XYZ(0.0, 0.0, deltaZ));
		try
		{
			return (sourceLine.CreateTransformed(translation) as Line) ?? sourceLine;
		}
		catch
		{
			return sourceLine;
		}
	}

	private static double GetViewElevation(View view)
	{
		if (view is ViewPlan { GenLevel: not null } viewPlan)
		{
			return viewPlan.GenLevel.ProjectElevation;
		}
		return view.Origin.Z;
	}

	private static bool TryApplyDimensionType(Dimension dimension, ElementId dimensionTypeId)
	{
		try
		{
			if (dimensionTypeId != null && dimensionTypeId != ElementId.InvalidElementId && dimension.GetTypeId() != dimensionTypeId)
			{
				dimension.ChangeTypeId(dimensionTypeId);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryApplyDimensionTextOverride(Dimension dimension, DimensionTextOverride textOverride)
	{
		try
		{
			dimension.Prefix = textOverride.Prefix;
			dimension.Suffix = textOverride.Suffix;
			dimension.Above = textOverride.Above;
			dimension.Below = textOverride.Below;
			dimension.ValueOverride = textOverride.ValueOverride;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryApplyDimensionSegmentOverrides(Dimension dimension, IReadOnlyList<DimensionSegmentOverride> segmentOverrides)
	{
		if (segmentOverrides.Count == 0)
		{
			return true;
		}
		try
		{
			DimensionSegmentArray segments = dimension.Segments;
			if (segments == null || segments.Size == 0)
			{
				return false;
			}
			bool allApplied = true;
			foreach (DimensionSegmentOverride segmentOverride in segmentOverrides)
			{
				if (segmentOverride.Index < 0 || segmentOverride.Index >= segments.Size)
				{
					allApplied = false;
					continue;
				}
				DimensionSegment segment = segments.get_Item(segmentOverride.Index);
				if (segment == null)
				{
					allApplied = false;
					continue;
				}
				try
				{
					segment.Prefix = segmentOverride.TextOverride.Prefix;
					segment.Suffix = segmentOverride.TextOverride.Suffix;
					segment.Above = segmentOverride.TextOverride.Above;
					segment.Below = segmentOverride.TextOverride.Below;
					segment.ValueOverride = segmentOverride.TextOverride.ValueOverride;
				}
				catch
				{
					allApplied = false;
				}
			}
			return allApplied;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryApplyGridStateToTarget(View targetView, Grid targetGrid, SourceGridState sourceState, TargetCopyStats targetStats, out string? issueDetail)
	{
		issueDetail = null;
		bool changedAny = false;
		try
		{
			if (IsModelExtentInView(targetGrid, targetView))
			{
				targetStats.Need2DConvert++;
				if (!TryConvertTargetGridTo2D(targetGrid, targetView))
				{
					targetStats.Convert2DFailed++;
					issueDetail = $"Grid '{targetGrid.Name}' (Id: {targetGrid.Id.Value}) convert 3D -> 2D failed";
					return false;
				}
				targetStats.ConvertedTo2D++;
				changedAny = true;
			}
			CurveApplyResult curveResult = TrySetCurveInView(targetGrid, targetView, sourceState.Curves);
			bool curveApplied = curveResult != CurveApplyResult.Failed;
			switch (curveResult)
			{
			case CurveApplyResult.SuccessDirect:
				targetStats.CurveDirectSetOk++;
				break;
			case CurveApplyResult.SuccessNormalized:
				targetStats.CurveNormalizedSetOk++;
				break;
			}
			if (!curveApplied)
			{
				targetStats.CurveFailed++;
			}
			else
			{
				changedAny = true;
			}
			bool end0BubbleApplied = SetBubbleVisibility(targetGrid, targetView, DatumEnds.End0, sourceState.End0BubbleVisible);
			bool end1BubbleApplied = SetBubbleVisibility(targetGrid, targetView, DatumEnds.End1, sourceState.End1BubbleVisible);
			bool bubbleApplied = end0BubbleApplied || end1BubbleApplied;
			if (!end0BubbleApplied || !end1BubbleApplied)
			{
				targetStats.BubbleFailed++;
			}
			if (bubbleApplied)
			{
				changedAny = true;
			}
			if (!changedAny)
			{
				issueDetail = $"Grid '{targetGrid.Name}' (Id: {targetGrid.Id.Value}) no changes could be applied";
				return false;
			}
			if (!curveApplied || !end0BubbleApplied || !end1BubbleApplied)
			{
				targetStats.PartialCopied++;
			}
			else
			{
				targetStats.Copied++;
			}
			return true;
		}
		catch
		{
			issueDetail = $"Grid '{targetGrid.Name}' (Id: {targetGrid.Id.Value}) unexpected error during apply";
			return false;
		}
	}

	private static CurveApplyResult TrySetCurveInView(Grid targetGrid, View targetView, IReadOnlyList<Curve> sourceCurves)
	{
		foreach (Curve curve in sourceCurves)
		{
			try
			{
				targetGrid.SetCurveInView(DatumExtentType.ViewSpecific, targetView, curve);
				return CurveApplyResult.SuccessDirect;
			}
			catch
			{
			}
		}
		List<Curve> targetCurves = (from curve2 in targetGrid.GetCurvesInView(DatumExtentType.ViewSpecific, targetView)
			where curve2 != null
			select curve2).ToList();
		if (targetCurves.Count == 0)
		{
			return CurveApplyResult.Failed;
		}
		foreach (Curve targetCurve in targetCurves)
		{
			foreach (Curve sourceCurve in sourceCurves)
			{
				Curve normalizedCurve = TryNormalizeCurveToTargetElevation(sourceCurve, targetCurve);
				if (!(normalizedCurve == null))
				{
					try
					{
						targetGrid.SetCurveInView(DatumExtentType.ViewSpecific, targetView, normalizedCurve);
						return CurveApplyResult.SuccessNormalized;
					}
					catch
					{
					}
				}
			}
		}
		return CurveApplyResult.Failed;
	}

	private static Curve? TryNormalizeCurveToTargetElevation(Curve sourceCurve, Curve targetCurve)
	{
		XYZ sourcePoint = GetRepresentativePoint(sourceCurve);
		XYZ targetPoint = GetRepresentativePoint(targetCurve);
		if (sourcePoint == null || targetPoint == null)
		{
			return null;
		}
		double deltaZ = targetPoint.Z - sourcePoint.Z;
		Transform translation = Transform.CreateTranslation(new XYZ(0.0, 0.0, deltaZ));
		try
		{
			return sourceCurve.CreateTransformed(translation);
		}
		catch
		{
			return null;
		}
	}

	private static XYZ? GetRepresentativePoint(Curve curve)
	{
		IList<XYZ> tessellated = curve.Tessellate();
		if (tessellated == null || tessellated.Count == 0)
		{
			return null;
		}
		return tessellated[tessellated.Count / 2];
	}

	private static bool IsModelExtentInView(Grid grid, View view)
	{
		DatumExtentType? end0 = TryGetDatumExtentType(grid, DatumEnds.End0, view);
		DatumExtentType? end1 = TryGetDatumExtentType(grid, DatumEnds.End1, view);
		return end0 != DatumExtentType.ViewSpecific || end1 != DatumExtentType.ViewSpecific;
	}

	private static bool TryConvertTargetGridTo2D(Grid targetGrid, View targetView)
	{
		try
		{
			targetGrid.SetDatumExtentType(DatumEnds.End0, targetView, DatumExtentType.ViewSpecific);
			targetGrid.SetDatumExtentType(DatumEnds.End1, targetView, DatumExtentType.ViewSpecific);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsBubbleVisibleSafe(Grid grid, View view, DatumEnds end)
	{
		try
		{
			return grid.IsBubbleVisibleInView(end, view);
		}
		catch
		{
			return false;
		}
	}

	private static bool SetBubbleVisibility(Grid grid, View view, DatumEnds datumEnd, bool show)
	{
		try
		{
			if (show)
			{
				grid.ShowBubbleInView(datumEnd, view);
			}
			else
			{
				grid.HideBubbleInView(datumEnd, view);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}
}
