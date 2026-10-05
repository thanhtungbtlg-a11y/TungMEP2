using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class TTCurve
{
	public static Curve PullOnto(this Curve curve, Plane? plane = null)
	{
		if (plane == null)
		{
			plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
		}
		XYZ pullStartPoint = curve.GetEndPoint(0).PullOnto(plane);
		XYZ pullEndPoint = curve.GetEndPoint(1).PullOnto(plane);
		return Line.CreateBound(pullStartPoint, pullEndPoint);
	}

	public static Curve CurveExtend(this Curve curve, double length)
	{
		XYZ startPoint = curve.GetEndPoint(0);
		XYZ endPoint = curve.GetEndPoint(1);
		double extensionLength = UnitUtils.ConvertToInternalUnits(length, UnitTypeId.Millimeters);
		XYZ direction = (endPoint - startPoint).Normalize().Multiply(extensionLength);
		XYZ newEndPoint = endPoint + direction;
		XYZ newStartPoint = startPoint + direction.Negate();
		return Line.CreateBound(newStartPoint, newEndPoint);
	}

	public static XYZ GetDirection(this Curve curve, bool bl = false)
	{
		XYZ startPoint = curve.GetEndPoint(0);
		XYZ endPoint = curve.GetEndPoint(1);
		if (!bl)
		{
			return (endPoint - startPoint).Normalize();
		}
		if (startPoint.Z > endPoint.Z)
		{
			return (endPoint - startPoint).Normalize();
		}
		return (startPoint - endPoint).Normalize();
	}
}
