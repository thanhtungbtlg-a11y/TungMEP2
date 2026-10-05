using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Helper;

public static class TTConnector
{
	public static List<Connector> ToList(this ConnectorSet cnSet)
	{
		List<Connector> data = new List<Connector>();
		foreach (Connector c in cnSet)
		{
			data.Add(c);
		}
		return data;
	}

	public static List<Connector> GetConnectorCloset(this Pipe pipe1, Pipe pipe2)
	{
		ConnectorSet cs1 = pipe1.ConnectorManager.Connectors;
		ConnectorSet cs2 = pipe2.ConnectorManager.Connectors;
		double distance = 10000.0;
		List<Connector> cn = new List<Connector>();
		foreach (Connector c1 in cs1)
		{
			foreach (Connector c2 in cs2)
			{
				double d = c1.Origin.DistanceTo(c2.Origin);
				if (d < distance)
				{
					cn = new List<Connector> { c1, c2 };
					distance = d;
				}
			}
		}
		return cn;
	}

	public static List<Connector> GetConnectorCloset(this Pipe pipe1, Pipe pipe2, Pipe pipe3)
	{
		ConnectorSet cs1 = pipe1.ConnectorManager.Connectors;
		ConnectorSet cs2 = pipe2.ConnectorManager.Connectors;
		ConnectorSet cs3 = pipe3.ConnectorManager.Connectors;
		double distance = 10000.0;
		List<Connector> cn = new List<Connector>();
		foreach (Connector c1 in cs1)
		{
			foreach (Connector c2 in cs2)
			{
				foreach (Connector c3 in cs3)
				{
					double d = (c1.Origin.DistanceTo(c2.Origin) + c1.Origin.DistanceTo(c3.Origin)) / 2.0;
					if (d < distance)
					{
						cn = new List<Connector> { c1, c2, c3 };
						distance = d;
					}
				}
			}
		}
		return cn;
	}

	public static List<Connector> GetConnectorCloset(this List<Connector> cn1, List<Connector> cn2)
	{
		Connector c1 = null;
		Connector c2 = null;
		double kc = 10000.0;
		foreach (Connector cc1 in cn1)
		{
			foreach (Connector cc2 in cn2)
			{
				double dem = cc1.Origin.DistanceTo(cc2.Origin);
				if (dem < kc)
				{
					c1 = cc1;
					c2 = cc2;
					kc = dem;
				}
			}
		}
		return new List<Connector> { c1, c2 };
	}

	public static List<Connector> GetConnectorCloset(this ConnectorSet cn1, ConnectorSet cn2)
	{
		Connector c1 = null;
		Connector c2 = null;
		double kc = 10000.0;
		foreach (Connector cc1 in cn1)
		{
			foreach (Connector cc2 in cn2)
			{
				double dem = cc1.Origin.DistanceTo(cc2.Origin);
				if (dem < kc)
				{
					c1 = cc1;
					c2 = cc2;
					kc = dem;
				}
			}
		}
		return new List<Connector> { c1, c2 };
	}

	public static Tuple<List<ElementId>, List<Connector>, List<Curve>> GetElementConnectedWithFitting(this FamilyInstance e)
	{
		ConnectorSet cnSetFromFitting = e.MEPModel.ConnectorManager.Connectors;
		List<ElementId> eConnectedWithFitting = new List<ElementId>();
		List<Connector> cnConnectedWithFitting = new List<Connector>();
		List<Curve> curveConnectedWithFitting = new List<Curve>();
		foreach (Connector c in cnSetFromFitting)
		{
			ConnectorSet cAllRefs = c.AllRefs;
			foreach (Connector cAllRef in cAllRefs)
			{
				cnConnectedWithFitting.Add(cAllRef);
				eConnectedWithFitting.Add(cAllRef.Owner.Id);
				LocationCurve locationCurve = cAllRef.Owner.Location as LocationCurve;
				curveConnectedWithFitting.Add(locationCurve.Curve);
			}
		}
		return Tuple.Create(eConnectedWithFitting, cnConnectedWithFitting, curveConnectedWithFitting);
	}

	public static void ConnectPipeAndPx(this Pipe pipe, FamilyInstance px)
	{
		ConnectorSet cs1 = pipe.ConnectorManager.Connectors;
		ConnectorSet cs2 = px.MEPModel.ConnectorManager.Connectors;
		double distance = 10000.0;
		List<Connector> cn = new List<Connector>();
		foreach (Connector c1 in cs1)
		{
			foreach (Connector c2 in cs2)
			{
				double d = c1.Origin.DistanceTo(c2.Origin);
				if (d < distance)
				{
					cn = new List<Connector> { c1, c2 };
					distance = d;
				}
			}
		}
		cn[0].ConnectTo(cn[1]);
	}
}
