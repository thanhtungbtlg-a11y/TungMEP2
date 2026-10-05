using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using VP_RevitAPI.Helper.Electrical;

namespace VP_RevitAPI.Internal.Electrical;

internal static class CircuitRasterOrderingService
{
	private sealed class RasterItem
	{
		internal ElementId ElementId { get; }

		internal XYZ? Point { get; }

		internal XYZ? NormalizedPoint { get; }

		internal RasterItem(ElementId elementId, XYZ? point, StartCorner startCorner)
		{
			ElementId = elementId;
			Point = point;
			NormalizedPoint = ((point == null) ? null : Normalize(point, startCorner));
		}

		private static XYZ Normalize(XYZ point, StartCorner startCorner)
		{
			if (1 == 0)
			{
			}
			XYZ result = startCorner switch
			{
				StartCorner.TopLeft => point, 
				StartCorner.TopRight => new XYZ(0.0 - point.X, point.Y, point.Z), 
				StartCorner.BottomLeft => new XYZ(point.X, 0.0 - point.Y, point.Z), 
				StartCorner.BottomRight => new XYZ(0.0 - point.X, 0.0 - point.Y, point.Z), 
				_ => point, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	private sealed class RawInputItem
	{
		internal Element Element { get; }

		internal XYZ? Point { get; }

		internal RawInputItem(Element element, XYZ? point)
		{
			Element = element;
			Point = point;
		}
	}

	private sealed class RowIndexedRasterItem
	{
		internal ElementId ElementId { get; }

		internal XYZ Point { get; }

		internal XYZ NormalizedPoint { get; }

		internal RowIndexedRasterItem(ElementId elementId, XYZ point, XYZ normalizedPoint)
		{
			ElementId = elementId;
			Point = point;
			NormalizedPoint = normalizedPoint;
		}
	}

	private sealed class RowBucket
	{
		internal int RowIndex { get; }

		internal double ReferenceY { get; }

		internal List<RowIndexedRasterItem> Items { get; } = new List<RowIndexedRasterItem>();

		internal RowBucket(int rowIndex, double referenceY)
		{
			RowIndex = rowIndex;
			ReferenceY = referenceY;
		}
	}

	private enum StartCorner
	{
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight
	}

	private enum ScanMode
	{
		HorizontalSnake
	}

	private enum SweepDirection
	{
		LeftToRight,
		RightToLeft
	}

	internal static List<ElementId> GetRasterOrderedLoadIds(Document document, ElectricalSystem electricalSystem, double rowToleranceMillimeters, WireRasterDebugLog? debugLog = null)
	{
		StartCorner startCorner = StartCorner.TopLeft;
		ScanMode scanMode = ScanMode.HorizontalSnake;
		if (scanMode != ScanMode.HorizontalSnake)
		{
			throw new NotSupportedException($"Unsupported scan mode: {scanMode}");
		}
		double rowToleranceInternal = UnitUtils.ConvertToInternalUnits(rowToleranceMillimeters, UnitTypeId.Millimeters);
		FamilyInstance panel = ElectricalSystemUtils.GetBaseEquipment(electricalSystem);
		debugLog?.Log($"Ordering config: startCorner={startCorner}, scanMode={scanMode}, rowToleranceMm={rowToleranceMillimeters:0.###}");
		debugLog?.Log(string.Format("Circuit id: {0}, panel id: {1}", electricalSystem.Id.Value, (panel == null) ? "<none>" : panel.Id.Value.ToString()));
		List<RawInputItem> rawItems = (from Element element in electricalSystem?.Elements
			where panel == null || element.Id != panel.Id
			select new RawInputItem(element, TryGetPoint(element))).ToList() ?? new List<RawInputItem>();
		foreach (RawInputItem raw in rawItems)
		{
			bool hasPoint = raw.Point != null;
			string pointText = (hasPoint ? $"({raw.Point.X:0.###}, {raw.Point.Y:0.###}, {raw.Point.Z:0.###})" : "<no LocationPoint>");
			debugLog?.Log($"Input element id={raw.Element.Id.Value}, hasLocationPoint={hasPoint}, point={pointText}");
		}
		List<RasterItem> points = (from rawInputItem in rawItems
			select new RasterItem(rawInputItem.Element.Id, rawInputItem.Point, startCorner) into rasterItem
			where rasterItem.Point != null
			select new RasterItem(rasterItem.ElementId, rasterItem.Point, startCorner) into rasterItem
			orderby rasterItem.NormalizedPoint.Y descending, rasterItem.NormalizedPoint.X, rasterItem.ElementId.Value
			select rasterItem).ToList();
		debugLog?.Log($"LocationPoint elements selected: {points.Count}");
		if (points.Count <= 1)
		{
			debugLog?.Log("Ordering short-circuited: fewer than 2 usable points.");
			return points.Select((RasterItem rasterItem) => rasterItem.ElementId).ToList();
		}
		List<double> sortedYs = points.Select((RasterItem rasterItem) => rasterItem.NormalizedPoint.Y).ToList();
		List<double> deltas = new List<double>();
		for (int index = 0; index < sortedYs.Count - 1; index++)
		{
			deltas.Add(Math.Abs(sortedYs[index] - sortedYs[index + 1]));
		}
		debugLog?.Log("Normalized Y values: " + string.Join(", ", sortedYs.Select((double y) => y.ToString("0.###"))));
		debugLog?.Log("Y deltas: " + string.Join(", ", deltas.Select((double delta) => delta.ToString("0.###"))));
		double? adaptiveThreshold = TryComputeAdaptiveRowGapThreshold(deltas, rowToleranceInternal);
		if (adaptiveThreshold.HasValue)
		{
			debugLog?.Log($"Row clustering mode: adaptive. threshold={adaptiveThreshold.Value:0.###}");
		}
		else
		{
			debugLog?.Log($"Row clustering mode: fixed tolerance fallback. threshold={rowToleranceInternal:0.###}");
		}
		List<RowBucket> rows = new List<RowBucket>();
		RasterItem firstItem = points[0];
		rows.Add(new RowBucket(0, firstItem.NormalizedPoint.Y));
		rows[0].Items.Add(new RowIndexedRasterItem(firstItem.ElementId, firstItem.Point, firstItem.NormalizedPoint));
		debugLog?.Log($"Row #0 <- element {firstItem.ElementId.Value}, normalized=({firstItem.NormalizedPoint.X:0.###},{firstItem.NormalizedPoint.Y:0.###})");
		for (int index2 = 1; index2 < points.Count; index2++)
		{
			RasterItem item = points[index2];
			RowBucket currentRow = rows[rows.Count - 1];
			double deltaY = Math.Abs(currentRow.ReferenceY - item.NormalizedPoint.Y);
			double effectiveThreshold = adaptiveThreshold ?? rowToleranceInternal;
			if (deltaY > effectiveThreshold)
			{
				currentRow = new RowBucket(rows.Count, item.NormalizedPoint.Y);
				rows.Add(currentRow);
				debugLog?.Log($"New row #{currentRow.RowIndex} created. referenceY={currentRow.ReferenceY:0.###}, deltaY={deltaY:0.###}");
			}
			currentRow.Items.Add(new RowIndexedRasterItem(item.ElementId, item.Point, item.NormalizedPoint));
			debugLog?.Log($"Row #{currentRow.RowIndex} <- element {item.ElementId.Value}, normalized=({item.NormalizedPoint.X:0.###},{item.NormalizedPoint.Y:0.###})");
		}
		List<ElementId> orderedIds = new List<ElementId>(points.Count);
		SweepDirection direction = SweepDirection.LeftToRight;
		foreach (RowBucket row in rows)
		{
			IEnumerable<RowIndexedRasterItem> rowItems = ((direction == SweepDirection.LeftToRight) ? (from rowItem in row.Items
				orderby rowItem.NormalizedPoint.X, rowItem.ElementId.Value
				select rowItem) : (from rowItem in row.Items
				orderby rowItem.NormalizedPoint.X descending, rowItem.ElementId.Value
				select rowItem));
			debugLog?.Log($"Sweep row #{row.RowIndex}: direction={direction}, count={row.Items.Count}");
			foreach (RowIndexedRasterItem item2 in rowItems)
			{
				orderedIds.Add(item2.ElementId);
				debugLog?.Log($"Ordered append: {item2.ElementId.Value}");
			}
			if (row.Items.Count >= 2)
			{
				direction = Flip(direction);
				debugLog?.Log($"Direction flipped for next row: {direction}");
			}
			else
			{
				debugLog?.Log("Direction kept because row has < 2 elements.");
			}
		}
		debugLog?.Log("Final ordered ids: " + string.Join(", ", orderedIds.Select((ElementId id) => id.Value.ToString())));
		return orderedIds;
	}

	private static SweepDirection Flip(SweepDirection direction)
	{
		return (direction == SweepDirection.LeftToRight) ? SweepDirection.RightToLeft : SweepDirection.LeftToRight;
	}

	private static XYZ? TryGetPoint(Element element)
	{
		if (!(element.Location is LocationPoint { Point: var point }))
		{
			return null;
		}
		return point;
	}

	private static double? TryComputeAdaptiveRowGapThreshold(IReadOnlyList<double> deltas, double fallbackTolerance)
	{
		List<double> positiveDeltas = (from delta in deltas
			where delta > 1E-06
			orderby delta
			select delta).ToList();
		if (positiveDeltas.Count < 2)
		{
			return null;
		}
		double minimumReference = Math.Max(fallbackTolerance * 0.1, 1E-06);
		int bestIndex = -1;
		double bestRatio = 0.0;
		for (int index = 0; index < positiveDeltas.Count - 1; index++)
		{
			double left = positiveDeltas[index];
			double right = positiveDeltas[index + 1];
			double ratio = right / Math.Max(left, minimumReference);
			if (!(ratio <= bestRatio))
			{
				bestRatio = ratio;
				bestIndex = index;
			}
		}
		if (bestIndex < 0)
		{
			return null;
		}
		double smallGap = positiveDeltas[bestIndex];
		double largeGap = positiveDeltas[bestIndex + 1];
		double jump = largeGap - smallGap;
		if (!(bestRatio >= 2.0) || !(jump >= fallbackTolerance * 0.75))
		{
			return null;
		}
		return (smallGap + largeGap) * 0.5;
	}
}
