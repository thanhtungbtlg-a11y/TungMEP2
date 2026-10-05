using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Helper;

public static class TTElement
{
	public static Line GetLocation(this Element e, bool check = false)
	{
		Curve curve = null;
		if (e.Category.BuiltInCategory == BuiltInCategory.OST_PipeCurves)
		{
			LocationCurve locationCurve = e.Location as LocationCurve;
			curve = locationCurve.Curve;
		}
		if (e.Category.BuiltInCategory == BuiltInCategory.OST_CableTray)
		{
			LocationCurve locationCurve2 = e.Location as LocationCurve;
			curve = locationCurve2.Curve;
		}
		XYZ startPoint = curve.GetEndPoint(0);
		XYZ endPoint = curve.GetEndPoint(1);
		if (check && startPoint.Z < endPoint.Z)
		{
			XYZ dem = startPoint;
			startPoint = endPoint;
			endPoint = dem;
		}
		return Line.CreateBound(startPoint, endPoint);
	}

	public static ConnectorSet? GetConnectors(this Element elem)
	{
		if (elem is FamilyInstance { MEPModel: var mepModel })
		{
			if (mepModel != null)
			{
				return mepModel.ConnectorManager.Connectors;
			}
		}
		else
		{
			if (elem is MEPCurve mepCurve)
			{
				return mepCurve.ConnectorManager.Connectors;
			}
			if (elem is Conduit conduit)
			{
				return conduit.ConnectorManager.Connectors;
			}
			if (elem is Pipe pipe)
			{
				return pipe.ConnectorManager.Connectors;
			}
			if (elem is Duct duct)
			{
				return duct.ConnectorManager.Connectors;
			}
		}
		return null;
	}
}
