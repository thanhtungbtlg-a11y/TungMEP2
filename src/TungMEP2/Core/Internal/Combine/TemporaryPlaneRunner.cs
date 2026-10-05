using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Combine;

internal static class TemporaryPlaneRunner
{
	private sealed class XyzComparer : IEqualityComparer<XYZ>
	{
		public bool Equals(XYZ? first, XYZ? second)
		{
			if (first == null || second == null)
			{
				return false;
			}
			return first.IsAlmostEqualTo(second, 1E-06);
		}

		public int GetHashCode(XYZ point)
		{
			int x = Math.Round(point.X / 1E-06).GetHashCode();
			int y = Math.Round(point.Y / 1E-06).GetHashCode();
			int z = Math.Round(point.Z / 1E-06).GetHashCode();
			return x ^ y ^ z;
		}
	}

	private const double PointTolerance = 1E-06;

	private const double ExtrusionThickness = 1E-07;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<Line> lines = CollectProjectedGridLines(document);
		if (lines.Count < 2)
		{
			TaskDialog.Show("TemporaryPlane", "Không đủ lưới thẳng để tạo mặt phẳng tạm.");
			return;
		}
		List<XYZ> points = GetIntersectionPoints(lines);
		if (points.Count < 2)
		{
			TaskDialog.Show("TemporaryPlane", "Không tìm được đủ giao điểm để tạo hình chữ nhật.");
			return;
		}
		Reference baseReference = uiDocument.Selection.PickObject(ObjectType.PointOnElement, "Chọn mặt làm cao độ gốc");
		XYZ basePoint = baseReference.GlobalPoint;
		UI_TemporaryPlaneOffsetWindow window = new UI_TemporaryPlaneOffsetWindow();
		if (window.ShowDialog() != true)
		{
			return;
		}
		if (!window.TryGetOffsetMillimeters(out var offsetMm))
		{
			TaskDialog.Show("TemporaryPlane", "Giá trị offset không hợp lệ.");
			return;
		}
		double offsetInternal = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
		double targetZ = basePoint.Z + offsetInternal;
		(XYZ point1, XYZ point2) furthestPoints = GetFurthestPoints(points);
		XYZ point1 = furthestPoints.point1;
		XYZ point2 = furthestPoints.point2;
		List<Curve> boundaryCurves = CreateRectangleBoundary(point1, point2, targetZ);
		CurveLoop curveLoop = CurveLoop.Create(boundaryCurves);
		List<CurveLoop> curveLoops = new List<CurveLoop> { curveLoop };
		Solid rectangleSurface = GeometryCreationUtilities.CreateExtrusionGeometry(curveLoops, XYZ.BasisZ, 1E-07);
		using Transaction transaction = new Transaction(document, "Create Temporary Plane");
		transaction.Start();
		ElementId categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
		DirectShape directShape = DirectShape.CreateElement(document, categoryId);
		directShape.SetShape(new List<GeometryObject> { rectangleSurface });
		((Element)directShape).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(offsetMm.ToString(CultureInfo.InvariantCulture));
		transaction.Commit();
	}

	private static List<Line> CollectProjectedGridLines(Document document)
	{
		return (from Grid grid in new FilteredElementCollector(document).OfClass(typeof(Grid))
			select grid.Curve as Line into line
			where line != null
			select line.PullOnto() as Line into projectedLine
			where projectedLine != null
			select (projectedLine)).ToList();
	}

	private static List<XYZ> GetIntersectionPoints(IList<Line> lines)
	{
		List<XYZ> points = new List<XYZ>();
		for (int i = 0; i < lines.Count; i++)
		{
			for (int j = i + 1; j < lines.Count; j++)
			{
				XYZ intersection = lines[i].FindIntersect(lines[j]);
				if (intersection != null)
				{
					points.Add(intersection);
				}
			}
		}
		return RemoveDuplicatePoints(points);
	}

	private static List<XYZ> RemoveDuplicatePoints(IEnumerable<XYZ> points)
	{
		HashSet<XYZ> uniquePoints = new HashSet<XYZ>(new XyzComparer());
		foreach (XYZ point in points)
		{
			uniquePoints.Add(point);
		}
		return uniquePoints.ToList();
	}

	private static (XYZ point1, XYZ point2) GetFurthestPoints(IReadOnlyList<XYZ> points)
	{
		if (points.Count < 2)
		{
			throw new ArgumentException("Danh sách điểm phải chứa ít nhất 2 điểm.");
		}
		XYZ furthestPoint1 = null;
		XYZ furthestPoint2 = null;
		double maxDistance = 0.0;
		foreach (XYZ first in points)
		{
			foreach (XYZ second in points)
			{
				double distance = first.DistanceTo(second);
				if (!(distance <= maxDistance))
				{
					furthestPoint1 = first;
					furthestPoint2 = second;
					maxDistance = distance;
				}
			}
		}
		return (point1: furthestPoint1, point2: furthestPoint2);
	}

	private static List<Curve> CreateRectangleBoundary(XYZ point1, XYZ point2, double z)
	{
		XYZ p1 = new XYZ(point1.X, point1.Y, z);
		XYZ p2 = new XYZ(point2.X, point2.Y, z);
		XYZ p3 = new XYZ(p1.X, p2.Y, z);
		XYZ p4 = new XYZ(p2.X, p1.Y, z);
		return new List<Curve>
		{
			Line.CreateBound(p1, p3),
			Line.CreateBound(p3, p2),
			Line.CreateBound(p2, p4),
			Line.CreateBound(p4, p1)
		};
	}
}
