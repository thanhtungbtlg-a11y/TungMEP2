using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Extensions;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal static class Align3DRunner
{
	internal static void Run(UIDocument uiDocument, Document document)
	{
		Isf.LocationCurve filter = new Isf.LocationCurve();
		while (true)
		{
			Reference r1 = uiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn phần tử 1 (mốc) - yêu cầu có LocationCurve");
			Reference r2 = uiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn phần tử 2 (cần dịch chuyển) - yêu cầu có LocationCurve");
			Element element1 = document.GetElement(r1);
			Element element2 = document.GetElement(r2);
			XYZ moveVector = GeometryUtilities.Align3D(element1, element2);
			if (moveVector.GetLength() <= 1E-09 && TryBuildFallbackMoveVector(element1, element2, out XYZ fallbackMoveVector))
			{
				moveVector = fallbackMoveVector;
			}
			if (!(moveVector.GetLength() <= 1E-09))
			{
				using Transaction tx = new Transaction(document, "Align 3D (Move Element2)");
				tx.Start();
				element2.Move(moveVector);
				tx.Commit();
			}
		}
	}

	private static bool TryBuildFallbackMoveVector(Element element1, Element element2, out XYZ moveVector)
	{
		moveVector = XYZ.Zero;
		if (!(element2.Location is LocationCurve locationCurve2))
		{
			return false;
		}
		if (!TryGetLocationDirection(locationCurve2, out XYZ direction2))
		{
			return false;
		}
		List<Connector> connectors1 = ConnectorUtilities.GetConnectors(element1);
		List<Connector> connectors2 = ConnectorUtilities.GetConnectors(element2);
		if (connectors1 == null || connectors2 == null)
		{
			return false;
		}
		List<Connector> closestConnectors = ConnectorUtilities.GetConnectorClose(connectors1, connectors2);
		if (closestConnectors == null || closestConnectors.Count < 2)
		{
			return false;
		}
		Connector connectorMain = closestConnectors[0];
		Connector connectorSub = closestConnectors[1];
		if (!TryGetConnectorNormal(connectorMain, out XYZ normal))
		{
			return false;
		}
		Plane plane = Plane.CreateByNormalAndOrigin(normal, connectorMain.Origin);
		double signedTarget = normal.DotProduct(plane.Origin - connectorSub.Origin);
		if (signedTarget * normal.DotProduct(direction2) < 0.0)
		{
			direction2 = -direction2;
		}
		double denom = normal.DotProduct(direction2);
		if (Math.Abs(denom) <= 1E-09)
		{
			return false;
		}
		double t = normal.DotProduct(plane.Origin - connectorSub.Origin) / denom;
		XYZ pointOnLineToPlane = connectorSub.Origin + t * direction2;
		moveVector = connectorMain.Origin - pointOnLineToPlane;
		return moveVector.GetLength() > 1E-09;
	}

	private static bool TryGetLocationDirection(LocationCurve locationCurve, out XYZ direction)
	{
		direction = XYZ.Zero;
		Curve curve = locationCurve.Curve;
		if (curve is Line { Direction: var lineDirection })
		{
			if (lineDirection.GetLength() <= 1E-09)
			{
				return false;
			}
			direction = lineDirection.Normalize();
			return true;
		}
		if (!curve.IsBound)
		{
			return false;
		}
		XYZ vector = curve.GetEndPoint(1) - curve.GetEndPoint(0);
		if (vector.GetLength() <= 1E-09)
		{
			return false;
		}
		direction = vector.Normalize();
		return true;
	}

	private static bool TryGetConnectorNormal(Connector connector, out XYZ normal)
	{
		normal = XYZ.Zero;
		try
		{
			XYZ basisZ = connector.CoordinateSystem?.BasisZ ?? XYZ.Zero;
			if (basisZ.GetLength() <= 1E-09)
			{
				return false;
			}
			normal = basisZ.Normalize();
			return true;
		}
		catch
		{
			return false;
		}
	}
}
