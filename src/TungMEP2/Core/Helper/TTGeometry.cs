using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class TTGeometry
{
	public static XYZ? FindIntersect(this Curve curve1, Curve curve2)
	{
		IntersectionResultArray results;
		SetComparisonResult result = curve1.Intersect(curve2, out results);
		if (result == SetComparisonResult.Overlap && results != null && results.Size > 0)
		{
			return results.get_Item(0).XYZPoint;
		}
		return null;
	}

	public static XYZ? FindIntersect(this Curve curve, Plane plane)
	{
		Line line = curve as Line;
		if (line == null)
		{
			return null;
		}
		plane.Project(line.Origin, out var uv1, out var distance);
		plane.Project(line.Origin + line.Direction, out var uv2, out distance);
		XYZ xyz1 = plane.Origin + uv1.U * plane.XVec + uv1.V * plane.YVec;
		XYZ xyz2 = plane.Origin + uv2.U * plane.XVec + uv2.V * plane.YVec;
		if (xyz1.IsAlmostEqualTo(xyz2))
		{
			return xyz1;
		}
		Line projectedLine = Line.CreateUnbound(xyz1, xyz2 - xyz1);
		if (line.Intersect(projectedLine, out var iResult) != SetComparisonResult.Disjoint)
		{
			return iResult.get_Item(0).XYZPoint;
		}
		return null;
	}
}
