using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Helper;

public static class ConnectorUtilities
{
	public static List<Connector>? GetConnectorClose(IList<Connector> connectors1, IList<Connector> connectors2)
	{
		if (connectors1 == null || connectors2 == null)
		{
			return null;
		}
		if (connectors1.Count == 0 || connectors2.Count == 0)
		{
			return null;
		}
		Connector closest1 = null;
		Connector closest2 = null;
		double minDistance = double.MaxValue;
		foreach (Connector connector1 in connectors1)
		{
			if (connector1 == null)
			{
				continue;
			}
			XYZ origin1 = connector1.Origin;
			foreach (Connector connector2 in connectors2)
			{
				if (connector2 != null)
				{
					double distance = (origin1 - connector2.Origin).GetLength();
					if (!(distance >= minDistance))
					{
						minDistance = distance;
						closest1 = connector1;
						closest2 = connector2;
					}
				}
			}
		}
		if (closest1 == null || closest2 == null)
		{
			return null;
		}
		return new List<Connector> { closest1, closest2 };
	}

	public static List<Connector>? GetConnectors(Element element)
	{
		if (element is MEPCurve mepCurve)
		{
			return mepCurve.ConnectorManager?.Connectors?.Cast<Connector>().ToList();
		}
		if (element is FamilyInstance familyInstance)
		{
			return (familyInstance.MEPModel?.ConnectorManager?.Connectors)?.Cast<Connector>().ToList();
		}
		return null;
	}

	public static Connector? FindClosestConnector(ConnectorSet connectorSet, XYZ target, bool endOnly, bool unconnectedOnly)
	{
		Connector closest = null;
		double minDistance = double.MaxValue;
		foreach (Connector connector in connectorSet)
		{
			if ((!endOnly || connector.ConnectorType == ConnectorType.End) && (!unconnectedOnly || !connector.IsConnected))
			{
				double distance = connector.Origin.DistanceTo(target);
				if (!(distance >= minDistance))
				{
					minDistance = distance;
					closest = connector;
				}
			}
		}
		return closest;
	}

	public static Connector GetClosestConnector(Pipe pipe, XYZ target)
	{
		ConnectorSet connectorSet = pipe.ConnectorManager.Connectors;
		return FindClosestConnector(connectorSet, target, endOnly: true, unconnectedOnly: true) ?? FindClosestConnector(connectorSet, target, endOnly: true, unconnectedOnly: false) ?? FindClosestConnector(connectorSet, target, endOnly: false, unconnectedOnly: false);
	}
}
