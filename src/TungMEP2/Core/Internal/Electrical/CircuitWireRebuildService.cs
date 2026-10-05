using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace VP_RevitAPI.Internal.Electrical;

internal static class CircuitWireRebuildService
{
	private sealed class CandidateRoute
	{
		internal string Label { get; }

		internal List<XYZ> Points { get; }

		internal CandidateRoute(string label, List<XYZ> points)
		{
			Label = label;
			Points = points;
		}
	}

	private const double LaneSpacingMillimeters = 100.0;

	private const double SameColumnDetourFactor = 2.0;

	private const int MaxLaneRetry = 12;

	private const double GeometryToleranceMillimeters = 20.0;

	internal static CircuitWireRebuildResult RebuildWiresBestEffort(Document document, View view, ElectricalSystem electricalSystem, IReadOnlyList<ElementId> orderedLoadIds, WireRasterDebugLog? debugLog = null)
	{
		CircuitWireRebuildResult result = new CircuitWireRebuildResult();
		debugLog?.Log($"Wire rebuild started. Circuit={electricalSystem.Id.Value}, View={view.Id.Value}, orderedCount={orderedLoadIds.Count}");
		if (orderedLoadIds.Count < 2)
		{
			debugLog?.Log("Wire rebuild stopped: ordered loads < 2.");
			return result;
		}
		HashSet<long> loadIdValues = new HashSet<long>(orderedLoadIds.Select((ElementId id) => id.Value));
		IList<ElementId> wireIdsToDelete = CollectWireIdsForLoadsInView(document, view, loadIdValues);
		debugLog?.Log($"Wires detected for delete in view: {wireIdsToDelete.Count}");
		if (wireIdsToDelete.Count > 0)
		{
			ICollection<ElementId> deleted = document.Delete(wireIdsToDelete);
			result.DeletedWireCount = deleted.Count;
			debugLog?.Log($"Deleted wire elements count: {result.DeletedWireCount}");
		}
		double laneSpacingInternal = UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);
		double geometryToleranceInternal = UnitUtils.ConvertToInternalUnits(20.0, UnitTypeId.Millimeters);
		ElementId wireTypeId = GetWireTypeId(document);
		if (wireTypeId == ElementId.InvalidElementId)
		{
			debugLog?.Log("No valid WireType found. Marking all ordered ids as failed.");
			foreach (ElementId loadId in orderedLoadIds)
			{
				result.FailedElementIds.Add(loadId);
			}
			return result;
		}
		debugLog?.Log($"Using wireTypeId: {wireTypeId.Value}. laneSpacingInternal={laneSpacingInternal:0.###}, tolInternal={geometryToleranceInternal:0.###}");
		for (int index = 0; index < orderedLoadIds.Count - 1; index++)
		{
			ElementId fromId = orderedLoadIds[index];
			ElementId toId = orderedLoadIds[index + 1];
			debugLog?.Log($"Pair {index + 1}/{orderedLoadIds.Count - 1}: from={fromId.Value} -> to={toId.Value}");
			Element fromElement = document.GetElement(fromId);
			Element toElement = document.GetElement(toId);
			if (fromElement == null || toElement == null)
			{
				RegisterFailedPair(result, fromId, toId);
				debugLog?.Log("Pair skipped: one of elements is null.");
				continue;
			}
			if (!TryGetClosestConnectorPair(fromElement, toElement, out Connector fromConnector, out Connector toConnector) || fromConnector == null || toConnector == null)
			{
				RegisterFailedPair(result, fromId, toId);
				debugLog?.Log("Pair skipped: no valid electrical connector pair.");
				continue;
			}
			debugLog?.Log($"Closest connector origins: from=({fromConnector.Origin.X:0.###},{fromConnector.Origin.Y:0.###},{fromConnector.Origin.Z:0.###}), to=({toConnector.Origin.X:0.###},{toConnector.Origin.Y:0.###},{toConnector.Origin.Z:0.###})");
			try
			{
				if (!TryBuildLanePath(fromConnector.Origin, toConnector.Origin, laneSpacingInternal, geometryToleranceInternal, debugLog, out List<XYZ> points) || points == null)
				{
					RegisterFailedPair(result, fromId, toId);
					debugLog?.Log("Pair skipped: no valid lane path found.");
					continue;
				}
				Wire.Create(document, wireTypeId, view.Id, WiringType.Chamfer, points, fromConnector, toConnector);
				result.CreatedWireCount++;
				debugLog?.Log("Pair created. points=[" + string.Join(" -> ", points.Select((XYZ point) => $"({point.X:0.###},{point.Y:0.###},{point.Z:0.###})")) + "]");
			}
			catch (Exception ex)
			{
				RegisterFailedPair(result, fromId, toId);
				debugLog?.Log("Pair failed during Wire.Create: " + ex.Message);
			}
		}
		debugLog?.Log($"Wire rebuild finished. deleted={result.DeletedWireCount}, created={result.CreatedWireCount}, failedCount={result.FailedElementIds.Count}");
		return result;
	}

	private static IList<ElementId> CollectWireIdsForLoadsInView(Document document, View view, ISet<long> loadIdValues)
	{
		List<ElementId> wireIds = new List<ElementId>();
		IEnumerable<Wire> wiresInView = new FilteredElementCollector(document, view.Id).OfClass(typeof(Wire)).Cast<Wire>();
		foreach (Wire wire in wiresInView)
		{
			if (IsWireConnectedToAnyLoad(wire, loadIdValues))
			{
				wireIds.Add(wire.Id);
			}
		}
		return wireIds;
	}

	private static bool IsWireConnectedToAnyLoad(Wire wire, ISet<long> loadIdValues)
	{
		ConnectorSet connectors = wire.ConnectorManager?.Connectors;
		if (connectors == null)
		{
			return false;
		}
		foreach (Connector connector in connectors)
		{
			ConnectorSet references = connector.AllRefs;
			foreach (Connector referenceConnector in references)
			{
				Element owner = referenceConnector.Owner;
				if (owner != null && loadIdValues.Contains(owner.Id.Value))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static ElementId GetWireTypeId(Document document)
	{
		return new FilteredElementCollector(document).OfClass(typeof(WireType)).Cast<WireType>().FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
	}

	private static void RegisterFailedPair(CircuitWireRebuildResult result, ElementId fromId, ElementId toId)
	{
		result.FailedElementIds.Add(fromId);
		result.FailedElementIds.Add(toId);
	}

	private static bool TryGetClosestConnectorPair(Element fromElement, Element toElement, out Connector? fromConnector, out Connector? toConnector)
	{
		fromConnector = null;
		toConnector = null;
		List<Connector> fromConnectors = GetElectricalConnectors(fromElement);
		List<Connector> toConnectors = GetElectricalConnectors(toElement);
		if (fromConnectors.Count == 0 || toConnectors.Count == 0)
		{
			return false;
		}
		double minDistance = double.MaxValue;
		foreach (Connector left in fromConnectors)
		{
			foreach (Connector right in toConnectors)
			{
				double distance = left.Origin.DistanceTo(right.Origin);
				if (!(distance >= minDistance))
				{
					minDistance = distance;
					fromConnector = left;
					toConnector = right;
				}
			}
		}
		return fromConnector != null && toConnector != null;
	}

	private static List<Connector> GetElectricalConnectors(Element element)
	{
		ConnectorSet connectors = null;
		if (element is FamilyInstance familyInstance)
		{
			connectors = familyInstance.MEPModel?.ConnectorManager?.Connectors;
		}
		else if (element is MEPCurve mepCurve)
		{
			connectors = mepCurve.ConnectorManager?.Connectors;
		}
		if (connectors == null)
		{
			return new List<Connector>();
		}
		return (from Connector connector in connectors
			where connector.Domain == Domain.DomainElectrical
			select connector).ToList();
	}

	private static bool TryBuildLanePath(XYZ from, XYZ to, double laneSpacingInternal, double geometryToleranceInternal, WireRasterDebugLog? debugLog, out List<XYZ>? pathPoints)
	{
		pathPoints = null;
		double minY = Math.Min(from.Y, to.Y);
		double baseLaneY = minY - laneSpacingInternal;
		for (int laneIndex = 0; laneIndex <= 12; laneIndex++)
		{
			double laneY = baseLaneY - (double)laneIndex * laneSpacingInternal;
			List<CandidateRoute> routes = BuildCandidateRoutes(from, to, laneY, laneSpacingInternal * 2.0, geometryToleranceInternal);
			using List<CandidateRoute>.Enumerator enumerator = routes.GetEnumerator();
			if (enumerator.MoveNext())
			{
				CandidateRoute route = enumerator.Current;
				debugLog?.Log($"Lane attempt {laneIndex}, route={route.Label}: selected");
				pathPoints = route.Points;
				return true;
			}
		}
		return false;
	}

	private static List<CandidateRoute> BuildCandidateRoutes(XYZ from, XYZ to, double laneY, double lateralOffset, double tolerance)
	{
		if (!(Math.Abs(from.X - to.X) <= tolerance))
		{
			XYZ p1 = new XYZ(from.X, laneY, from.Z);
			XYZ p2 = new XYZ(to.X, laneY, to.Z);
			List<CandidateRoute> routes = new List<CandidateRoute>
			{
				new CandidateRoute("Standard", new List<XYZ> { from, p1, p2, to })
			};
			XYZ h1 = new XYZ(to.X, from.Y, from.Z);
			routes.Add(new CandidateRoute("HorizontalFirst-Direct", new List<XYZ> { from, h1, to }));
			double leftDetourX = Math.Min(from.X, to.X) - lateralOffset;
			double rightDetourX = Math.Max(from.X, to.X) + lateralOffset;
			routes.Add(BuildHorizontalFirstDetourRoute(from, to, leftDetourX, "HorizontalFirst-LeftDetour"));
			routes.Add(BuildHorizontalFirstDetourRoute(from, to, rightDetourX, "HorizontalFirst-RightDetour"));
			return routes;
		}
		List<CandidateRoute> sameColumnRoutes = new List<CandidateRoute>();
		double[] array = new double[2] { -1.0, 1.0 };
		foreach (double sign in array)
		{
			double xDetour = from.X + sign * lateralOffset;
			XYZ p3 = new XYZ(xDetour, from.Y, from.Z);
			XYZ p4 = new XYZ(xDetour, to.Y, to.Z);
			sameColumnRoutes.Add(new CandidateRoute((sign < 0.0) ? "SameColumn-LeftDetourHorizontalFirst" : "SameColumn-RightDetourHorizontalFirst", new List<XYZ> { from, p3, p4, to }));
		}
		return sameColumnRoutes;
	}

	private static CandidateRoute BuildHorizontalFirstDetourRoute(XYZ from, XYZ to, double detourX, string label)
	{
		XYZ p1 = new XYZ(detourX, from.Y, from.Z);
		XYZ p2 = new XYZ(detourX, to.Y, to.Z);
		return new CandidateRoute(label, new List<XYZ> { from, p1, p2, to });
	}
}
