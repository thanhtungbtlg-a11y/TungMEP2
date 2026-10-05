using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_DimGridByBoundary : SecuredExternalCommand
{
	private enum Side
	{
		Top,
		Bottom,
		Left,
		Right
	}

	private enum AxisAlignment
	{
		Unknown,
		Horizontal,
		Vertical
	}

	private sealed class GridLineInfo
	{
		public Grid Grid { get; }

		public Curve Curve { get; }

		public GridLineInfo(Grid grid, Curve curve)
		{
			Grid = grid;
			Curve = curve;
		}
	}

	private sealed class GridWithIntersection
	{
		public Grid Grid { get; }

		public XYZ IntersectionPoint { get; }

		public GridWithIntersection(Grid grid, XYZ intersectionPoint)
		{
			Grid = grid;
			IntersectionPoint = intersectionPoint;
		}
	}

	private const double DirectionTolerance = 0.99;

	private const double CoincidentTolerance = 1E-06;

	protected override void ExecuteCore()
	{
		try
		{
			if (!TryGetTargetFloorPlan(out ViewPlan viewPlan))
			{
				TaskDialog.Show("DimGrid", "Hãy chọn đúng 1 Floor Plan hoặc mở một Floor Plan trước khi chạy lệnh.");
				return;
			}
			UI_GridDimensionOptionsWindow window = new UI_GridDimensionOptionsWindow();
			if (window.ShowDialog() != true)
			{
				return;
			}
			if (!window.TryGetOptions(out GridDimensionOptions options))
			{
				TaskDialog.Show("DimGrid", "Thông số DIM không hợp lệ.");
				return;
			}
			ViewPlan targetView = viewPlan;
			List<GridLineInfo> gridInfos = CollectGridInfos(targetView);
			if (gridInfos.Count == 0)
			{
				TaskDialog.Show("DimGrid", "Không tìm thấy grid trong view.");
				return;
			}
			if (!TryBuildBoundaryLinesFromGridExtents(targetView, gridInfos, out List<Line> edges, out string boundaryError))
			{
				TaskDialog.Show("DimGrid", boundaryError);
				return;
			}
			using Transaction transaction = new Transaction(base.Document, "DIM Grid By Boundary");
			transaction.Start();
			ConvertAllGridsTo2DAndApplyBubble(targetView, gridInfos, edges, options);
			gridInfos = CollectGridInfos(targetView);
			DeleteToolDimensions(targetView);
			List<string> messages = new List<string>();
			CreateSideDimensions(targetView, Side.Top, options.Top, options.TopOffsetMillimeters, edges[0], gridInfos, messages);
			CreateSideDimensions(targetView, Side.Bottom, options.Bottom, options.BottomOffsetMillimeters, edges[1], gridInfos, messages);
			CreateSideDimensions(targetView, Side.Right, options.Right, options.RightOffsetMillimeters, edges[2], gridInfos, messages);
			CreateSideDimensions(targetView, Side.Left, options.Left, options.LeftOffsetMillimeters, edges[3], gridInfos, messages);
			transaction.Commit();
			if (messages.Count > 0)
			{
				TaskDialog.Show("DimGrid", string.Join(Environment.NewLine, messages));
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("DimGrid", ex2.ToString());
		}
	}

	private bool TryGetTargetFloorPlan(out ViewPlan? viewPlan)
	{
		viewPlan = null;
		List<ViewPlan> selectedPlans = (from plan in (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id)).OfType<ViewPlan>()
			where !plan.IsTemplate && plan.ViewType == ViewType.FloorPlan
			select plan).ToList();
		if (selectedPlans.Count == 1)
		{
			viewPlan = selectedPlans[0];
			return true;
		}
		if (selectedPlans.Count > 1)
		{
			return false;
		}
		if (base.Document.ActiveView is ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } activePlan)
		{
			viewPlan = activePlan;
			return true;
		}
		return false;
	}

	private bool TryBuildBoundaryLinesFromGridExtents(View view, IReadOnlyList<GridLineInfo> gridInfos, out List<Line> boundaries, out string errorMessage)
	{
		boundaries = new List<Line>();
		errorMessage = string.Empty;
		List<GridLineInfo> horizontalGrids = gridInfos.Where((GridLineInfo info) => GetAxisAlignment(info.Curve, view) == AxisAlignment.Horizontal).ToList();
		List<GridLineInfo> verticalGrids = gridInfos.Where((GridLineInfo info) => GetAxisAlignment(info.Curve, view) == AxisAlignment.Vertical).ToList();
		if (horizontalGrids.Count < 2 || verticalGrids.Count < 2)
		{
			errorMessage = "Không đủ grid ngang/dọc để dựng biên DIM từ grid.";
			return false;
		}
		GridLineInfo topGrid = horizontalGrids.OrderByDescending((GridLineInfo info) => GetViewVerticalValue(GetCurveCenter(info.Curve), view)).First();
		GridLineInfo bottomGrid = horizontalGrids.OrderBy((GridLineInfo info) => GetViewVerticalValue(GetCurveCenter(info.Curve), view)).First();
		GridLineInfo leftGrid = verticalGrids.OrderBy((GridLineInfo info) => GetViewHorizontalValue(GetCurveCenter(info.Curve), view)).First();
		GridLineInfo rightGrid = verticalGrids.OrderByDescending((GridLineInfo info) => GetViewHorizontalValue(GetCurveCenter(info.Curve), view)).First();
		if (!TryIntersectUnbound(topGrid.Curve, leftGrid.Curve, out XYZ topLeft) || !TryIntersectUnbound(topGrid.Curve, rightGrid.Curve, out XYZ topRight) || !TryIntersectUnbound(bottomGrid.Curve, leftGrid.Curve, out XYZ bottomLeft) || !TryIntersectUnbound(bottomGrid.Curve, rightGrid.Curve, out XYZ bottomRight))
		{
			errorMessage = "Không dựng được 4 góc từ 2 cặp grid biên.";
			return false;
		}
		boundaries.Add(Line.CreateBound(topLeft, topRight));
		boundaries.Add(Line.CreateBound(bottomLeft, bottomRight));
		boundaries.Add(Line.CreateBound(bottomRight, topRight));
		boundaries.Add(Line.CreateBound(bottomLeft, topLeft));
		return true;
	}

	private List<GridLineInfo> CollectGridInfos(View view)
	{
		return (from Grid grid in new FilteredElementCollector(base.Document, view.Id).OfClass(typeof(Grid))
			select new
			{
				Grid = grid,
				Curve = GetGridCurveInView(grid, view)
			} into item
			where item.Curve != null
			select new GridLineInfo(item.Grid, item.Curve)).ToList();
	}

	private static Curve? GetGridCurveInView(Grid grid, View view)
	{
		Curve viewSpecific = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).FirstOrDefault();
		if (viewSpecific != null)
		{
			return viewSpecific;
		}
		return grid.GetCurvesInView(DatumExtentType.Model, view).FirstOrDefault();
	}

	private void CreateSideDimensions(View view, Side side, bool isEnabled, double offsetMillimeters, Line boundaryLine, IReadOnlyList<GridLineInfo> gridInfos, List<string> messages)
	{
		if (!isEnabled)
		{
			return;
		}
		Side side2 = side;
		bool flag = (uint)(side2 - 2) <= 1u;
		bool needHorizontalGrid = flag;
		XYZ intersectionPoint;
		List<GridWithIntersection> sideGrids = (from info in gridInfos
			where needHorizontalGrid ? (GetAxisAlignment(info.Curve, view) == AxisAlignment.Horizontal) : (GetAxisAlignment(info.Curve, view) == AxisAlignment.Vertical)
			select TryGetGridIntersectionOnBoundary(info.Curve, boundaryLine, out intersectionPoint) ? new GridWithIntersection(info.Grid, intersectionPoint) : null into item
			where item != null
			select (item) into item
			orderby GetBoundaryAxisValue(item.IntersectionPoint, side, view)
			select item).ToList();
		if (sideGrids.Count < 2)
		{
			messages.Add(ToDisplaySide(side) + ": không đủ grid để tạo DIM.");
			return;
		}
		double offsetInternal = UnitUtils.ConvertToInternalUnits(offsetMillimeters, UnitTypeId.Millimeters);
		if (offsetInternal < 0.0)
		{
			messages.Add(ToDisplaySide(side) + ": offset không hợp lệ.");
			return;
		}
		if (1 == 0)
		{
		}
		XYZ xYZ = side switch
		{
			Side.Top => view.UpDirection, 
			Side.Bottom => -view.UpDirection, 
			Side.Right => view.RightDirection, 
			Side.Left => -view.RightDirection, 
			_ => XYZ.Zero, 
		};
		if (1 == 0)
		{
		}
		XYZ direction = xYZ;
		Line chainLine = (boundaryLine.CreateTransformed(Transform.CreateTranslation(direction.Multiply(offsetInternal))) as Line) ?? boundaryLine;
		Line overallLine = (boundaryLine.CreateTransformed(Transform.CreateTranslation(direction.Multiply(offsetInternal * 2.0))) as Line) ?? boundaryLine;
		ReferenceArray chainReferences = new ReferenceArray();
		foreach (GridWithIntersection gridInfo in sideGrids)
		{
			chainReferences.Append(new Reference(gridInfo.Grid));
		}
		Dimension chainDimension = base.Document.Create.NewDimension(view, chainLine, chainReferences);
		ReferenceArray overallReferences = new ReferenceArray();
		overallReferences.Append(new Reference(sideGrids[0].Grid));
		overallReferences.Append(new Reference(sideGrids[sideGrids.Count - 1].Grid));
		Dimension overallDimension = base.Document.Create.NewDimension(view, overallLine, overallReferences);
		OverrideGraphicSettings overrideGraphicSettings = new OverrideGraphicSettings();
		overrideGraphicSettings.SetHalftone(halftone: true);
		view.SetElementOverrides(chainDimension.Id, overrideGraphicSettings);
		view.SetElementOverrides(overallDimension.Id, overrideGraphicSettings);
		chainDimension.Pinned = true;
		overallDimension.Pinned = true;
	}

	private void ConvertAllGridsTo2DAndApplyBubble(View view, IReadOnlyList<GridLineInfo> gridInfos, IReadOnlyList<Line> edges, GridDimensionOptions options)
	{
		foreach (GridLineInfo gridInfo in gridInfos)
		{
			try
			{
				gridInfo.Grid.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
				gridInfo.Grid.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
			}
			catch
			{
			}
		}
		base.Document.Regenerate();
		List<GridLineInfo> refreshedGridInfos = CollectGridInfos(view);
		foreach (GridLineInfo gridInfo2 in refreshedGridInfos)
		{
			switch (GetAxisAlignment(gridInfo2.Curve, view))
			{
			case AxisAlignment.Horizontal:
				ApplyHorizontalGrid2D(view, gridInfo2.Grid, gridInfo2.Curve, edges[3], edges[2], options);
				break;
			case AxisAlignment.Vertical:
				ApplyVerticalGrid2D(view, gridInfo2.Grid, gridInfo2.Curve, edges[1], edges[0], options);
				break;
			}
		}
	}

	private void ApplyHorizontalGrid2D(View view, Grid grid, Curve sourceCurve, Line leftBoundary, Line rightBoundary, GridDimensionOptions options)
	{
		if (TryGetGridIntersectionOnBoundary(sourceCurve, leftBoundary, out XYZ leftPoint) && TryGetGridIntersectionOnBoundary(sourceCurve, rightBoundary, out XYZ rightPoint))
		{
			XYZ rightDirection = view.RightDirection.Normalize();
			double leftOffset = (options.BubbleLeft ? UnitUtils.ConvertToInternalUnits(options.BubbleLeftOffsetMillimeters, UnitTypeId.Millimeters) : 0.0);
			double rightOffset = (options.BubbleRight ? UnitUtils.ConvertToInternalUnits(options.BubbleRightOffsetMillimeters, UnitTypeId.Millimeters) : 0.0);
			XYZ newLeft = leftPoint - rightDirection.Multiply(leftOffset);
			XYZ newRight = rightPoint + rightDirection.Multiply(rightOffset);
			if (TryCreateCoincidentGridLine(sourceCurve, newLeft, newRight, out Line newLine))
			{
				grid.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);
				SetBubbleVisibility(grid, view, DatumEnds.End0, options.BubbleLeft);
				SetBubbleVisibility(grid, view, DatumEnds.End1, options.BubbleRight);
			}
		}
	}

	private void ApplyVerticalGrid2D(View view, Grid grid, Curve sourceCurve, Line bottomBoundary, Line topBoundary, GridDimensionOptions options)
	{
		if (TryGetGridIntersectionOnBoundary(sourceCurve, bottomBoundary, out XYZ bottomPoint) && TryGetGridIntersectionOnBoundary(sourceCurve, topBoundary, out XYZ topPoint))
		{
			XYZ upDirection = view.UpDirection.Normalize();
			double bottomOffset = (options.BubbleBottom ? UnitUtils.ConvertToInternalUnits(options.BubbleBottomOffsetMillimeters, UnitTypeId.Millimeters) : 0.0);
			double topOffset = (options.BubbleTop ? UnitUtils.ConvertToInternalUnits(options.BubbleTopOffsetMillimeters, UnitTypeId.Millimeters) : 0.0);
			XYZ newBottom = bottomPoint - upDirection.Multiply(bottomOffset);
			XYZ newTop = topPoint + upDirection.Multiply(topOffset);
			if (TryCreateCoincidentGridLine(sourceCurve, newBottom, newTop, out Line newLine))
			{
				grid.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);
				SetBubbleVisibility(grid, view, DatumEnds.End0, options.BubbleBottom);
				SetBubbleVisibility(grid, view, DatumEnds.End1, options.BubbleTop);
			}
		}
	}

	private static void SetBubbleVisibility(Grid grid, View view, DatumEnds datumEnd, bool show)
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
		}
		catch
		{
		}
	}

	private static bool TryCreateCoincidentGridLine(Curve sourceCurve, XYZ point1, XYZ point2, out Line? line)
	{
		line = null;
		if (!(sourceCurve is Line sourceLine))
		{
			return false;
		}
		double sourceZ = sourceLine.GetEndPoint(0).Z;
		XYZ p1 = new XYZ(point1.X, point1.Y, sourceZ);
		XYZ p2 = new XYZ(point2.X, point2.Y, sourceZ);
		if (p1.DistanceTo(p2) <= 1E-06)
		{
			return false;
		}
		Line unboundSource = Line.CreateUnbound(sourceLine.GetEndPoint(0), sourceLine.Direction);
		if (unboundSource.Distance(p1) > 1E-06 || unboundSource.Distance(p2) > 1E-06)
		{
			return false;
		}
		line = Line.CreateBound(p1, p2);
		return line != null;
	}

	private void DeleteToolDimensions(View view)
	{
		List<Dimension> dimensions = new FilteredElementCollector(base.Document, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
		foreach (Dimension dimension in dimensions)
		{
			OverrideGraphicSettings settings = view.GetElementOverrides(dimension.Id);
			if (settings.Halftone)
			{
				dimension.Pinned = false;
				base.Document.Delete(dimension.Id);
			}
		}
	}

	private static bool TryIntersectUnbound(Curve firstCurve, Curve secondCurve, out XYZ intersectionPoint)
	{
		intersectionPoint = XYZ.Zero;
		if (!(firstCurve is Line firstLine) || !(secondCurve is Line secondLine))
		{
			return false;
		}
		Line projectedFirst = firstLine.PullOnto() as Line;
		Line projectedSecond = secondLine.PullOnto() as Line;
		if (projectedFirst == null || projectedSecond == null)
		{
			return false;
		}
		Line unboundFirst = Line.CreateUnbound(projectedFirst.GetEndPoint(0), projectedFirst.Direction);
		Line unboundSecond = Line.CreateUnbound(projectedSecond.GetEndPoint(0), projectedSecond.Direction);
		XYZ intersection = unboundFirst.FindIntersect(unboundSecond);
		if (intersection == null)
		{
			return false;
		}
		intersectionPoint = intersection;
		return true;
	}

	private static bool TryGetGridIntersectionOnBoundary(Curve gridCurve, Line boundaryLine, out XYZ intersectionPoint)
	{
		intersectionPoint = XYZ.Zero;
		if (!(gridCurve is Line gridLine))
		{
			return false;
		}
		Line projectedGrid = gridLine.PullOnto() as Line;
		Line projectedBoundary = boundaryLine.PullOnto() as Line;
		if (projectedGrid == null || projectedBoundary == null)
		{
			return false;
		}
		Line unboundGrid = Line.CreateUnbound(projectedGrid.GetEndPoint(0), projectedGrid.Direction);
		Line unboundBoundary = Line.CreateUnbound(projectedBoundary.GetEndPoint(0), projectedBoundary.Direction);
		XYZ intersection = unboundGrid.FindIntersect(unboundBoundary);
		if (intersection == null)
		{
			return false;
		}
		intersectionPoint = intersection;
		return true;
	}

	private static double GetBoundaryAxisValue(XYZ point, Side side, View view)
	{
		if (1 == 0)
		{
		}
		double result = side switch
		{
			Side.Top => GetViewHorizontalValue(point, view), 
			Side.Bottom => GetViewHorizontalValue(point, view), 
			Side.Left => GetViewVerticalValue(point, view), 
			Side.Right => GetViewVerticalValue(point, view), 
			_ => 0.0, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static AxisAlignment GetAxisAlignment(Curve curve, View view)
	{
		if (!(curve is Line line))
		{
			return AxisAlignment.Unknown;
		}
		XYZ direction = line.Direction.Normalize();
		XYZ right = view.RightDirection.Normalize();
		XYZ up = view.UpDirection.Normalize();
		if (Math.Abs(direction.DotProduct(right)) >= 0.99)
		{
			return AxisAlignment.Horizontal;
		}
		if (Math.Abs(direction.DotProduct(up)) >= 0.99)
		{
			return AxisAlignment.Vertical;
		}
		return AxisAlignment.Unknown;
	}

	private static XYZ GetCurveCenter(Curve curve)
	{
		return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2.0;
	}

	private static double GetViewHorizontalValue(XYZ point, View view)
	{
		XYZ originToPoint = point - view.Origin;
		return originToPoint.DotProduct(view.RightDirection);
	}

	private static double GetViewVerticalValue(XYZ point, View view)
	{
		XYZ originToPoint = point - view.Origin;
		return originToPoint.DotProduct(view.UpDirection);
	}

	private static string ToDisplaySide(Side side)
	{
		if (1 == 0)
		{
		}
		string result = side switch
		{
			Side.Top => "Trên", 
			Side.Bottom => "Dưới", 
			Side.Left => "Trái", 
			Side.Right => "Phải", 
			_ => side.ToString(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
