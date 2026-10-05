using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Helper.COMBINE;

internal static class RerouteUnderOverService
{
	private readonly struct ConnectorReferenceLocator
	{
		public ElementId OwnerId { get; }

		public XYZ Origin { get; }

		public Domain Domain { get; }

		public ConnectorType ConnectorType { get; }

		public ConnectorReferenceLocator(ElementId ownerId, XYZ origin, Domain domain, ConnectorType connectorType)
		{
			OwnerId = ownerId;
			Origin = origin;
			Domain = domain;
			ConnectorType = connectorType;
		}
	}

	private const double MinSegmentLength = 0.0001;

	private const double SlopeTolerance = 0.02;

	public static bool IsSupportedStraightMepCurve(Element? element)
	{
		if (!(element is MEPCurve))
		{
			return false;
		}
		if (!(element.Location is LocationCurve locationCurve))
		{
			return false;
		}
		return locationCurve.Curve is Line;
	}

	public static List<double> GetAllowedAnglesDegrees(Document document, IEnumerable<Element> elements)
	{
		List<double> values = new List<double>();
		List<int> categories = (from e in elements
			select e.Category?.Id.IntegerValue into id
			where id.HasValue
			select id.Value).Distinct().ToList();
		foreach (int categoryId in categories)
		{
			if (categoryId == -2008132 || categoryId == -2008130)
			{
				foreach (double angle in document.Settings.ElectricalSetting.GetSpecificFittingAngles())
				{
					if (document.Settings.ElectricalSetting.GetSpecificFittingAngleStatus(angle))
					{
						values.Add(ToDegrees(angle));
					}
				}
			}
			if (categoryId == -2008044)
			{
				PipeSettings pipeSettings = PipeSettings.GetPipeSettings(document);
				foreach (double angle2 in pipeSettings.GetSpecificFittingAngles())
				{
					if (pipeSettings.GetSpecificFittingAngleStatus(angle2))
					{
						values.Add(ToDegrees(angle2));
					}
				}
			}
			if (categoryId != -2008000)
			{
				continue;
			}
			DuctSettings ductSettings = DuctSettings.GetDuctSettings(document);
			foreach (double angle3 in ductSettings.GetSpecificFittingAngles())
			{
				if (ductSettings.GetSpecificFittingAngleStatus(angle3))
				{
					values.Add(ToDegrees(angle3));
				}
			}
		}
		if (values.Count == 0)
		{
			values.AddRange(new double[5] { 22.5, 30.0, 45.0, 60.0, 90.0 });
		}
		return (from v in values.Select((double v) => Math.Round(v, 3)).Distinct()
			orderby v
			select v).ToList();
	}

	public static bool TryGetSuggestedVerticalOffsetMillimeters(IEnumerable<Element> elements, out double offsetMillimeters)
	{
		offsetMillimeters = 0.0;
		double maxOffsetInternal = 0.0;
		foreach (Element element in elements)
		{
			if (TryGetBaseSizeInternal(element, out var sizeInternal, out var factor) && !(sizeInternal <= 1E-09))
			{
				double candidate = factor * sizeInternal;
				if (candidate > maxOffsetInternal)
				{
					maxOffsetInternal = candidate;
				}
			}
		}
		if (maxOffsetInternal <= 1E-09)
		{
			return false;
		}
		offsetMillimeters = UnitUtils.ConvertFromInternalUnits(maxOffsetInternal, UnitTypeId.Millimeters);
		return true;
	}

	public static bool TryReroute(Document document, Element element, XYZ firstPickPoint, XYZ secondPickPoint, double verticalOffsetMillimeters, double angleDegrees, out string error)
	{
		error = string.Empty;
		if (!IsSupportedStraightMepCurve(element))
		{
			error = "Only straight Pipe, Duct, Conduit, and CableTray are supported.";
			return false;
		}
		if (element.Pinned)
		{
			error = "Selected element is pinned.";
			return false;
		}
		if (!(element.Location is LocationCurve { Curve: Line hostLine }))
		{
			error = "Cannot resolve element line.";
			return false;
		}
		XYZ lineDirection = hostLine.Direction.Normalize();
		if (Math.Abs(lineDirection.Z) > 0.02)
		{
			error = "This command currently supports near-horizontal segments only.";
			return false;
		}
		if (Math.Abs(verticalOffsetMillimeters) < 1E-09)
		{
			error = "Vertical offset cannot be zero.";
			return false;
		}
		if (angleDegrees <= 0.0 || angleDegrees > 90.0)
		{
			error = "Angle must be in range (0, 90].";
			return false;
		}
		if (!TryResolveBreakPoints(hostLine, firstPickPoint, secondPickPoint, out XYZ break1, out XYZ break2, out error))
		{
			return false;
		}
		double offsetInternal = UnitUtils.ConvertToInternalUnits(verticalOffsetMillimeters, UnitTypeId.Millimeters);
		double angleRadians = UnitUtils.ConvertToInternalUnits(angleDegrees, UnitTypeId.Degrees);
		double tangent = Math.Tan(angleRadians);
		if (Math.Abs(tangent) < 1E-09)
		{
			error = "Angle is too small to construct the reroute.";
			return false;
		}
		double runIn = Math.Abs(offsetInternal) / tangent;
		double totalLength = break1.DistanceTo(break2);
		if (totalLength <= 2.0 * runIn + 0.0001)
		{
			error = "Not enough straight length between break points for this offset and angle.";
			return false;
		}
		XYZ zOffset = XYZ.BasisZ.Multiply(offsetInternal);
		XYZ top1 = break1 + lineDirection.Multiply(runIn) + zOffset;
		XYZ top2 = break2 - lineDirection.Multiply(runIn) + zOffset;
		if (top1.DistanceTo(top2) <= 0.0001)
		{
			error = "Computed top segment is too short.";
			return false;
		}
		if (!TryBreakAndRemoveMiddle(document, element, break1, break2, out Element leftElement, out Element rightElement, out error))
		{
			return false;
		}
		if (leftElement == null || rightElement == null)
		{
			error = "Failed to resolve side elements after break operation.";
			return false;
		}
		if (!TryCreateCurveLike(document, element, break1, top1, out Element segment1, out error) || !TryCreateCurveLike(document, element, top1, top2, out Element segment2, out error) || !TryCreateCurveLike(document, element, top2, break2, out Element segment3, out error))
		{
			return false;
		}
		if (segment1 == null || segment2 == null || segment3 == null)
		{
			error = "Failed to create reroute segments.";
			return false;
		}
		if (!TryConnect(leftElement, break1, segment1, break1, document, out error) || !TryConnect(segment1, top1, segment2, top1, document, out error) || !TryConnect(segment2, top2, segment3, top2, document, out error) || !TryConnect(segment3, break2, rightElement, break2, document, out error))
		{
			return false;
		}
		return true;
	}

	private static bool TryResolveBreakPoints(Line hostLine, XYZ firstPick, XYZ secondPick, out XYZ break1, out XYZ break2, out string error)
	{
		error = string.Empty;
		break1 = XYZ.Zero;
		break2 = XYZ.Zero;
		XYZ start = hostLine.GetEndPoint(0);
		XYZ end = hostLine.GetEndPoint(1);
		XYZ direction = (end - start).Normalize();
		double length = hostLine.Length;
		Line unbound = Line.CreateUnbound(start, direction);
		XYZ projected1 = unbound.Project(firstPick).XYZPoint;
		XYZ projected2 = unbound.Project(secondPick).XYZPoint;
		double t1 = (projected1 - start).DotProduct(direction);
		double t2 = (projected2 - start).DotProduct(direction);
		double guard = Math.Max(0.0001, length * 0.001);
		t1 = Clamp(t1, guard, length - guard);
		t2 = Clamp(t2, guard, length - guard);
		if (Math.Abs(t2 - t1) <= 0.0002)
		{
			error = "Break points are too close to each other.";
			return false;
		}
		if (t1 > t2)
		{
			double num = t2;
			t2 = t1;
			t1 = num;
		}
		break1 = start + direction.Multiply(t1);
		break2 = start + direction.Multiply(t2);
		return true;
	}

	private static bool TryBreakAndRemoveMiddle(Document document, Element originalElement, XYZ break1, XYZ break2, out Element? leftElement, out Element? rightElement, out string error)
	{
		leftElement = null;
		rightElement = null;
		error = string.Empty;
		if (!TryBreakCurve(document, originalElement, break1, out ElementId newId1, out error))
		{
			return false;
		}
		ElementId secondBreakHostId = ResolveSecondBreakHost(document, originalElement.Id, newId1, break2);
		Element secondBreakHost = document.GetElement(secondBreakHostId);
		if (secondBreakHost == null)
		{
			error = "Failed to resolve second break host segment.";
			return false;
		}
		if (!TryBreakCurve(document, secondBreakHost, break2, out ElementId newId2, out error))
		{
			return false;
		}
		List<ElementId> segmentIds = new List<ElementId> { originalElement.Id, newId1, newId2 }.Distinct().ToList();
		segmentIds = segmentIds.Where((ElementId id) => document.GetElement(id) != null).ToList();
		if (segmentIds.Count < 3)
		{
			error = "Failed to create three segments after break operations.";
			return false;
		}
		XYZ center = (break1 + break2).Multiply(0.5);
		ElementId middleId = segmentIds.OrderBy((ElementId id) => DistanceFromPointToElementLine(document.GetElement(id), center)).First();
		document.Delete(middleId);
		segmentIds.Remove(middleId);
		if (segmentIds.Count != 2)
		{
			error = "Failed to resolve side segments after deleting middle segment.";
			return false;
		}
		Element first = document.GetElement(segmentIds[0]);
		Element second = document.GetElement(segmentIds[1]);
		if (first == null || second == null)
		{
			error = "Failed to resolve side segments.";
			return false;
		}
		double firstToBreak1 = DistanceFromPointToElementLine(first, break1);
		double secondToBreak1 = DistanceFromPointToElementLine(second, break1);
		if (firstToBreak1 <= secondToBreak1)
		{
			leftElement = first;
			rightElement = second;
		}
		else
		{
			leftElement = second;
			rightElement = first;
		}
		return true;
	}

	private static ElementId ResolveSecondBreakHost(Document document, ElementId id1, ElementId id2, XYZ break2)
	{
		Element first = document.GetElement(id1);
		Element second = document.GetElement(id2);
		if (first == null)
		{
			return id2;
		}
		if (second == null)
		{
			return id1;
		}
		double d1 = DistanceFromPointToElementLine(first, break2);
		double d2 = DistanceFromPointToElementLine(second, break2);
		return (d1 <= d2) ? id1 : id2;
	}

	private static double DistanceFromPointToElementLine(Element? element, XYZ point)
	{
		if (!(element?.Location is LocationCurve { Curve: Line line }))
		{
			return double.MaxValue;
		}
		return line.Distance(point);
	}

	private static bool TryBreakCurve(Document document, Element element, XYZ point, out ElementId newId, out string error)
	{
		newId = ElementId.InvalidElementId;
		error = string.Empty;
		try
		{
			if (element is Pipe)
			{
				newId = PlumbingUtils.BreakCurve(document, element.Id, point);
			}
			else if (element is Duct)
			{
				newId = MechanicalUtils.BreakCurve(document, element.Id, point);
			}
			else
			{
				if (!(element is Conduit) && !(element is CableTray))
				{
					error = "Unsupported element type.";
					return false;
				}
				if (!TryBreakElectricalCurve(document, element, point, out newId, out error))
				{
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
		if (newId == ElementId.InvalidElementId)
		{
			error = "BreakCurve returned invalid element id.";
			return false;
		}
		return true;
	}

	private static bool TryBreakElectricalCurve(Document document, Element element, XYZ point, out ElementId newId, out string error)
	{
		newId = ElementId.InvalidElementId;
		error = string.Empty;
		if (!(element.Location is LocationCurve { Curve: Line hostLine } locationCurve))
		{
			error = "Cannot resolve electrical curve geometry for split.";
			return false;
		}
		XYZ start = hostLine.GetEndPoint(0);
		XYZ end = hostLine.GetEndPoint(1);
		XYZ direction = (end - start).Normalize();
		Line unbound = Line.CreateUnbound(start, direction);
		XYZ breakPoint = unbound.Project(point).XYZPoint;
		if (breakPoint.DistanceTo(start) <= 0.0001 || breakPoint.DistanceTo(end) <= 0.0001)
		{
			error = "Break point is too close to element endpoint.";
			return false;
		}
		Connector endConnectorBefore = FindClosestEndConnector(element, end);
		if (endConnectorBefore == null)
		{
			error = "Cannot find end connector to preserve electrical connections.";
			return false;
		}
		List<ConnectorReferenceLocator> references = GetExternalConnectedReferences(endConnectorBefore, element.Id);
		try
		{
			locationCurve.Curve = Line.CreateBound(start, breakPoint);
			document.Regenerate();
			if (!TryCreateCurveLike(document, element, breakPoint, end, out Element newElement, out error))
			{
				return false;
			}
			if (newElement == null)
			{
				error = "Failed to create electrical segment after split.";
				return false;
			}
			Connector newEndConnector = FindClosestEndConnector(newElement, end);
			if (newEndConnector == null)
			{
				error = "Cannot find connector on newly created electrical segment.";
				return false;
			}
			foreach (ConnectorReferenceLocator reference in references)
			{
				if (TryResolveConnector(document, reference, out Connector targetConnector) && targetConnector != null)
				{
					try
					{
						newEndConnector.ConnectTo(targetConnector);
					}
					catch
					{
					}
				}
			}
			newId = newElement.Id;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static List<ConnectorReferenceLocator> GetExternalConnectedReferences(Connector connector, ElementId sourceOwnerId)
	{
		List<ConnectorReferenceLocator> result = new List<ConnectorReferenceLocator>();
		foreach (Connector reference in connector.AllRefs.Cast<Connector>())
		{
			if (!(reference.Owner?.Id == sourceOwnerId) && reference.Owner != null)
			{
				result.Add(new ConnectorReferenceLocator(reference.Owner.Id, reference.Origin, reference.Domain, reference.ConnectorType));
			}
		}
		return result;
	}

	private static bool TryResolveConnector(Document document, ConnectorReferenceLocator locator, out Connector? connector)
	{
		connector = null;
		Element owner = document.GetElement(locator.OwnerId);
		if (owner == null)
		{
			return false;
		}
		List<Connector> connectors = ConnectorUtilities.GetConnectors(owner);
		if (connectors == null || connectors.Count == 0)
		{
			return false;
		}
		connector = (from c in connectors
			where c.Domain == locator.Domain
			orderby c.Origin.DistanceTo(locator.Origin)
			select c).FirstOrDefault();
		if (connector == null)
		{
			connector = connectors.OrderBy((Connector c) => c.Origin.DistanceTo(locator.Origin)).FirstOrDefault();
		}
		return connector != null;
	}

	private static bool TryCreateCurveLike(Document document, Element template, XYZ start, XYZ end, out Element? created, out string error)
	{
		created = null;
		error = string.Empty;
		try
		{
			if (template is Pipe pipe)
			{
				ElementId systemTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
				ElementId levelId = pipe.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
				if (levelId == ElementId.InvalidElementId)
				{
					levelId = FindNearestLevelId(document, start.Z);
				}
				Pipe newPipe = Pipe.Create(document, systemTypeId, pipe.GetTypeId(), levelId, start, end);
				((Element)newPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble());
				created = newPipe;
				return true;
			}
			if (template is Duct duct)
			{
				ElementId systemTypeId2 = ((Element)duct).get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM).AsElementId();
				ElementId levelId2 = duct.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
				if (levelId2 == ElementId.InvalidElementId)
				{
					levelId2 = FindNearestLevelId(document, start.Z);
				}
				Duct newDuct = Duct.Create(document, systemTypeId2, duct.GetTypeId(), levelId2, start, end);
				Parameter diameter = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
				Parameter width = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
				Parameter height = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
				if (diameter != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM).Set(diameter.AsDouble());
				}
				if (width != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).Set(width.AsDouble());
				}
				if (height != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).Set(height.AsDouble());
				}
				created = newDuct;
				return true;
			}
			if (template is Conduit { LevelId: var levelId3 } conduit)
			{
				if (levelId3 == ElementId.InvalidElementId)
				{
					levelId3 = FindNearestLevelId(document, start.Z);
				}
				Conduit newConduit = Conduit.Create(document, conduit.GetTypeId(), start, end, levelId3);
				((Element)newConduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).Set(((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble());
				created = newConduit;
				return true;
			}
			if (template is CableTray { LevelId: var levelId4 } cableTray)
			{
				if (levelId4 == ElementId.InvalidElementId)
				{
					levelId4 = FindNearestLevelId(document, start.Z);
				}
				CableTray newCableTray = CableTray.Create(document, cableTray.GetTypeId(), start, end, levelId4);
				((Element)newCableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).Set(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble());
				((Element)newCableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).Set(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble());
				created = newCableTray;
				return true;
			}
			error = "Unsupported curve type for create operation.";
			return false;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool TryConnect(Element first, XYZ firstPoint, Element second, XYZ secondPoint, Document document, out string error)
	{
		error = string.Empty;
		Connector firstConnector = FindClosestEndConnector(first, firstPoint);
		Connector secondConnector = FindClosestEndConnector(second, secondPoint);
		if (firstConnector == null || secondConnector == null)
		{
			error = "Cannot find required connectors for reroute.";
			return false;
		}
		if (firstConnector.IsConnected && secondConnector.IsConnected)
		{
			return true;
		}
		try
		{
			document.Create.NewElbowFitting(firstConnector, secondConnector);
			return true;
		}
		catch
		{
			try
			{
				firstConnector.ConnectTo(secondConnector);
				return true;
			}
			catch (Exception ex)
			{
				error = ((ex.Message == "failed to insert elbow.") ? "The routing solution failed because no default fitting is specified or the fitting cannot be found in the project. Ensure the fitting is loaded and try again." : ex.Message);
				return false;
			}
		}
	}

	private static bool TryGetBaseSizeInternal(Element element, out double sizeInternal, out double factor)
	{
		sizeInternal = 0.0;
		factor = 4.0;
		if (element is Pipe pipe)
		{
			sizeInternal = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
			return sizeInternal > 1E-09;
		}
		if (element is Conduit conduit)
		{
			sizeInternal = ((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
			return sizeInternal > 1E-09;
		}
		if (element is Duct duct)
		{
			double diameter = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
			if (diameter > 1E-09)
			{
				sizeInternal = diameter;
				factor = 4.0;
				return true;
			}
			double width = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0.0;
			double height = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
			if (width <= 1E-09 || height <= 1E-09)
			{
				return false;
			}
			sizeInternal = Math.Min(width, height);
			factor = 3.0;
			return true;
		}
		if (element is CableTray cableTray)
		{
			double width2 = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
			double height2 = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
			if (width2 <= 1E-09 || height2 <= 1E-09)
			{
				return false;
			}
			sizeInternal = Math.Min(width2, height2);
			factor = 3.0;
			return true;
		}
		return false;
	}

	private static Connector? FindClosestEndConnector(Element element, XYZ point)
	{
		List<Connector> connectors = ConnectorUtilities.GetConnectors(element);
		if (connectors == null || connectors.Count == 0)
		{
			return null;
		}
		return (from c in connectors
			where c.ConnectorType == ConnectorType.End
			orderby c.Origin.DistanceTo(point)
			select c).FirstOrDefault();
	}

	private static ElementId FindNearestLevelId(Document document, double z)
	{
		return (from Level l in new FilteredElementCollector(document).OfClass(typeof(Level))
			orderby Math.Abs(l.Elevation - z)
			select l).FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
	}

	private static double Clamp(double value, double min, double max)
	{
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	private static double ToDegrees(double value)
	{
		if (Math.Abs(value) <= 6.283186307179586)
		{
			return value * 180.0 / Math.PI;
		}
		return value;
	}
}
