using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Commands.CTN;

internal static class ElbowRunService
{
	private const double DirectionTolerance = 1E-09;

	private const double DefaultLengthMm = 300.0;

	public static bool Execute(UIDocument uiDocument, Document document, ElbowRunDirection direction, out string error)
	{
		error = string.Empty;
		PipeSlopeRibbonSelection? ribbonSlope = null;
		if (direction == ElbowRunDirection.Left || direction == ElbowRunDirection.Right)
		{
			RevitPipeSlopeRibbonState.TryGetCurrentOrLast(out ribbonSlope);
		}
		Reference selectedReference = uiDocument.Selection.PickObject(ObjectType.PointOnElement, new Isf.Sample(Domain.DomainUndefined), "Please pick a point on Duct, Pipe, Conduit or Cable Tray.");
		Element element = document.GetElement(selectedReference);
		if (!TryGetSupportedElement(element, out Element mepElement, out error))
		{
			return false;
		}
		if (mepElement.Pinned)
		{
			error = "Selected element is pinned.";
			return false;
		}
		if (!(mepElement.Location is LocationCurve { Curve: Line locationLine }))
		{
			error = "Only straight MEP curves are supported.";
			return false;
		}
		List<Connector> endConnectors = ConnectorUtilities.GetConnectors(mepElement)?.Where((Connector c) => c.ConnectorType == ConnectorType.End).ToList() ?? new List<Connector>();
		if (endConnectors.Count == 0)
		{
			error = "This element has no usable end connector.";
			return false;
		}
		XYZ pickPoint = selectedReference.GlobalPoint;
		Connector startConnector = (from c in endConnectors
			where !c.IsConnected
			orderby c.Origin.DistanceTo(pickPoint)
			select c).FirstOrDefault();
		if (startConnector == null)
		{
			error = "This element is already connected on both sides.";
			return false;
		}
		XYZ origin = startConnector.Origin;
		double length = ((mepElement is Pipe pipe) ? GetPipeElbowLengthInternal(pipe) : GetRunLengthBySizeInternal(mepElement));
		XYZ end = ComputeEndPoint(
			mepElement,
			startConnector,
			locationLine,
			direction,
			length,
			ribbonSlope);
		ElementId levelId = GetLevelId(mepElement, document, origin.Z);
		if (levelId == ElementId.InvalidElementId)
		{
			error = "Cannot resolve valid level for new segment.";
			return false;
		}
		Element created = CreateRunElement(document, mepElement, origin, end, levelId, out error);
		if (created == null)
		{
			return false;
		}
		document.Regenerate();
		List<Connector> newEndConnectors = ConnectorUtilities.GetConnectors(created)?.Where((Connector c) => c.ConnectorType == ConnectorType.End).ToList() ?? new List<Connector>();
		Connector newConnector = newEndConnectors.OrderBy((Connector c) => c.Origin.DistanceTo(origin)).FirstOrDefault();
		if (newConnector == null)
		{
			error = "Cannot find connector on newly created run.";
			return false;
		}
		try
		{
			document.Create.NewElbowFitting(startConnector, newConnector);
			return true;
		}
		catch
		{
			try
			{
				startConnector.ConnectTo(newConnector);
				return true;
			}
			catch
			{
				error = "Cannot create elbow and run in this direction.";
				return false;
			}
		}
	}

	private static XYZ ComputeEndPoint(
		Element element,
		Connector startConnector,
		Line locationLine,
		ElbowRunDirection direction,
		double length,
		PipeSlopeRibbonSelection? ribbonSlope)
	{
		XYZ origin = startConnector.Origin;
		XYZ forward = GetForwardDirection(startConnector, locationLine);
		XYZ right = GetRightDirection(startConnector, forward);
		XYZ up = XYZ.BasisZ;
		if (1 == 0)
		{
		}
		XYZ xYZ = direction switch
		{
			ElbowRunDirection.Up => up, 
			ElbowRunDirection.Down => up.Negate(), 
			ElbowRunDirection.Left => right.Negate(), 
			ElbowRunDirection.Right => right, 
			ElbowRunDirection.Up45 => (forward + up).Normalize(), 
			ElbowRunDirection.Down45 => (forward - up).Normalize(), 
			_ => forward, 
		};
		if (1 == 0)
		{
		}
		XYZ directionVector = xYZ;
		if (element is Pipe && (direction == ElbowRunDirection.Left || direction == ElbowRunDirection.Right))
		{
			double horizontal = Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
			double existingSignedRatio = horizontal > 1E-09 &&
				Math.Abs(forward.Z) < 0.999999999
				? forward.Z / horizontal
				: 0.0;
			double signedRatio = ribbonSlope == null
				? existingSignedRatio
				: ElbowRunSlopeMath.ResolveSignedRatio(
					ribbonSlope.Ratio,
					ribbonSlope.Mode,
					existingSignedRatio);
			XYZ horizontalRight = new XYZ(right.X, right.Y, 0.0);
			if (horizontalRight.GetLength() > 1E-09)
			{
				horizontalRight = horizontalRight.Normalize();
				if (direction == ElbowRunDirection.Left)
				{
					horizontalRight = horizontalRight.Negate();
				}
				double deltaZ = ElbowRunSlopeMath.CalculateRise(length, signedRatio);
				return origin + horizontalRight.Multiply(length) + XYZ.BasisZ.Multiply(deltaZ);
			}
		}
		return origin + directionVector.Multiply(length);
	}

	private static XYZ GetForwardDirection(Connector connector, Line locationLine)
	{
		XYZ end0 = locationLine.GetEndPoint(0);
		XYZ end1 = locationLine.GetEndPoint(1);
		XYZ lineDirection = (end1 - end0).Normalize();
		XYZ forward = ((connector.Origin.DistanceTo(end0) <= connector.Origin.DistanceTo(end1)) ? lineDirection.Negate() : lineDirection);
		if (forward.GetLength() < 1E-09)
		{
			XYZ basis = connector.CoordinateSystem.BasisZ;
			if (basis.GetLength() > 1E-09)
			{
				forward = basis.Normalize();
			}
		}
		return forward;
	}

	private static XYZ GetRightDirection(Connector connector, XYZ forward)
	{
		XYZ candidate = connector.CoordinateSystem.BasisX;
		XYZ right = candidate - forward.Multiply(candidate.DotProduct(forward));
		if (right.GetLength() > 1E-09)
		{
			return right.Normalize();
		}
		XYZ fallback = XYZ.BasisZ.CrossProduct(forward);
		if (fallback.GetLength() > 1E-09)
		{
			return fallback.Normalize();
		}
		fallback = connector.CoordinateSystem.BasisY;
		right = fallback - forward.Multiply(fallback.DotProduct(forward));
		if (right.GetLength() > 1E-09)
		{
			return right.Normalize();
		}
		return XYZ.BasisX;
	}

	private static bool TryGetSupportedElement(Element element, out Element mepElement, out string error)
	{
		error = string.Empty;
		mepElement = element;
		if ((element is Pipe || element is Duct || element is Conduit || element is CableTray) ? true : false)
		{
			return true;
		}
		error = "Please select a Pipe, Duct, Conduit, or Cable Tray.";
		return false;
	}

	private static ElementId GetLevelId(Element sample, Document document, double z)
	{
		if (sample is Pipe pipe)
		{
			return pipe.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
		}
		if (sample is Duct duct)
		{
			return duct.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
		}
		if (sample is Conduit conduit && conduit.LevelId != ElementId.InvalidElementId)
		{
			return conduit.LevelId;
		}
		if (sample is CableTray cableTray && cableTray.LevelId != ElementId.InvalidElementId)
		{
			return cableTray.LevelId;
		}
		return FindNearestLevelId(document, z);
	}

	private static ElementId FindNearestLevelId(Document document, double z)
	{
		return (from Level l in new FilteredElementCollector(document).OfClass(typeof(Level))
			orderby Math.Abs(l.Elevation - z)
			select l).FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
	}

	private static double GetRunLengthBySizeInternal(Element sample)
	{
		double baseLength = UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters);
		double size = GetScaleSize(sample);
		return Math.Max(baseLength, 3.0 * size);
	}

	private static double GetPipeElbowLengthInternal(Pipe pipe)
	{
		double diameterInternal = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		string key = ((int)Math.Round(UnitUtils.ConvertFromInternalUnits(diameterInternal, UnitTypeId.Millimeters))).ToString(CultureInfo.InvariantCulture);
		string raw = GetData.FromJson("ElbowData", key);
		double oldLengthInternal;
		if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lengthMm))
		{
			oldLengthInternal = UnitUtils.ConvertToInternalUnits(lengthMm, UnitTypeId.Millimeters);
		}
		else
		{
			double fallback = UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters);
			oldLengthInternal = Math.Max(fallback, 3.0 * diameterInternal);
		}
		return oldLengthInternal * 3.0;
	}

	private static double GetScaleSize(Element sample)
	{
		if (sample is Duct duct)
		{
			Parameter diameter = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
			if (diameter != null)
			{
				return diameter.AsDouble();
			}
			Parameter width = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
			Parameter height = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
			return Math.Max(width?.AsDouble() ?? 0.0, height?.AsDouble() ?? 0.0);
		}
		if (sample is Conduit conduit)
		{
			return ((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble();
		}
		if (sample is CableTray cableTray)
		{
			double width2 = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble();
			double height2 = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble();
			return Math.Max(width2, height2);
		}
		return UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);
	}

	private static Element? CreateRunElement(Document document, Element sample, XYZ start, XYZ end, ElementId levelId, out string error)
	{
		error = string.Empty;
		try
		{
			if (sample is Pipe pipe)
			{
				ElementId systemTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
				Pipe newPipe = Pipe.Create(document, systemTypeId, pipe.GetTypeId(), levelId, start, end);
				((Element)newPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble());
				return newPipe;
			}
			if (sample is Duct duct)
			{
				ElementId systemTypeId2 = ((Element)duct).get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM).AsElementId();
				Duct newDuct = Duct.Create(document, systemTypeId2, duct.GetTypeId(), levelId, start, end);
				Parameter diameter = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
				if (diameter != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM).Set(diameter.AsDouble());
				}
				Parameter width = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
				if (width != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).Set(width.AsDouble());
				}
				Parameter height = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
				if (height != null)
				{
					((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).Set(height.AsDouble());
				}
				return newDuct;
			}
			if (sample is Conduit conduit)
			{
				Conduit newConduit = Conduit.Create(document, conduit.GetTypeId(), start, end, levelId);
				((Element)newConduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).Set(((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble());
				return newConduit;
			}
			if (sample is CableTray cableTray)
			{
				CableTray newTray = CableTray.Create(document, cableTray.GetTypeId(), start, end, levelId);
				((Element)newTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).Set(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble());
				((Element)newTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).Set(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble());
				return newTray;
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return null;
		}
		error = "Unsupported MEP curve type.";
		return null;
	}
}
