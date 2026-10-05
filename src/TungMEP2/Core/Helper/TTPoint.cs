using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class TTPoint
{
	public static XYZ PullOnto(this XYZ pt, Plane? plane = null)
	{
		if (plane == null)
		{
			plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
		}
		XYZ originToProjectionPoint = pt.Subtract(plane.Origin);
		double distanceToPlane = plane.Normal.DotProduct(originToProjectionPoint);
		return pt.Subtract(plane.Normal.Multiply(distanceToPlane));
	}

	public static XYZ? PullOntoPlaneByVector(this XYZ point, Plane plane, XYZ vector)
	{
		XYZ planeNormal = plane.Normal;
		XYZ planeOrigin = plane.Origin;
		double distanceToPlane = (planeOrigin - point).DotProduct(planeNormal) / planeNormal.DotProduct(vector);
		try
		{
			return point + vector.Multiply(distanceToPlane);
		}
		catch
		{
			return null;
		}
	}
}
