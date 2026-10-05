using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal static class AlignCurveRunner
{
	private sealed class Runner
	{
		private readonly record struct CurvePoints(XYZ Start, XYZ End, XYZ Direction);

		private const string DialogTitle = "Align Curve";

		private const double Epsilon = 1E-09;

		private static readonly IReadOnlyList<double> AllowedAngles = new double[3]
		{
			0.0,
			Math.PI / 4.0,
			Math.PI / 2.0
		};

		private readonly UIDocument _uiDocument;

		private readonly Document _document;

		private UIDocument UiDocument => _uiDocument;

		private Document Document => _document;

		internal Runner(UIDocument uiDocument, Document document)
		{
			_uiDocument = uiDocument;
			_document = document;
		}

		internal void Run()
		{
			try
			{
				if (!(Document.ActiveView is ViewPlan) && !(Document.ActiveView is ViewSection))
				{
					TaskDialog.Show("Align Curve", "Please run this command in plan view / section.");
					return;
				}
				List<MEPCurve> selectedCurves = (from curve in (from id in UiDocument.Selection.GetElementIds()
						select Document.GetElement(id)).OfType<MEPCurve>()
					group curve by curve.Id into @group
					select @group.First()).ToList();
				if (selectedCurves.Count == 0)
				{
					TaskDialog.Show("Align Curve", "Please select valid elements.");
					return;
				}
				while (true)
				{
					XYZ point;
					try
					{
						point = PickPointByView(Document.ActiveView);
					}
					catch (Autodesk.Revit.Exceptions.OperationCanceledException)
					{
						break;
					}
					using Transaction transaction = new Transaction(Document, "Align Curve");
					transaction.Start();
					try
					{
						bool isAligned = IsAlignedToPoint(Document.ActiveView, point, selectedCurves);
						double angle = 0.0;
						if (isAligned)
						{
							selectedCurves = OrderMEPCurveByPoints(Document.ActiveView, selectedCurves, point);
							angle = GetSnappedAngle(Document.ActiveView, selectedCurves, point);
						}
						foreach (MEPCurve mepCurve in selectedCurves)
						{
							if (TryGetCurvePoints(mepCurve, out var curvePoints))
							{
								XYZ planeNormal = Document.ActiveView.ViewDirection.CrossProduct(curvePoints.Direction.CrossProduct(Document.ActiveView.ViewDirection));
								XYZ lineIntersection = GetPlaneLineIntersection(point, planeNormal, curvePoints.Start, curvePoints.Direction);
								if (lineIntersection.GetLength() < 50000.0)
								{
									AdjustMEPCurveLocation(mepCurve, lineIntersection);
								}
							}
						}
						if (isAligned && angle > 1E-09)
						{
							selectedCurves = ApplyPowerTurn(Document.ActiveView, selectedCurves, point, angle);
						}
						transaction.Commit();
					}
					catch
					{
						transaction.RollBack();
					}
				}
				UiDocument.Selection.SetElementIds(selectedCurves.Select((MEPCurve curve) => curve.Id).ToList());
			}
			catch (Autodesk.Revit.Exceptions.OperationCanceledException)
			{
			}
			catch (Exception ex3)
			{
				TaskDialog.Show("Align Curve", ex3.ToString());
			}
		}

		private XYZ PickPointByView(View activeView)
		{
			if (!(activeView is ViewSection sectionView))
			{
				return UiDocument.Selection.PickPoint(ObjectSnapTypes.Endpoints);
			}
			using Transaction transaction = new Transaction(Document, "Temp Set Workplane");
			transaction.Start();
			Plane plane = Plane.CreateByNormalAndOrigin(sectionView.ViewDirection, sectionView.Origin);
			SketchPlane sketchPlane = SketchPlane.Create(Document, plane);
			activeView.SketchPlane = sketchPlane;
			activeView.ShowActiveWorkPlane();
			XYZ picked = UiDocument.Selection.PickPoint(ObjectSnapTypes.Endpoints);
			transaction.RollBack();
			return picked;
		}

		private static double GetSnappedAngle(View view, IList<MEPCurve> curves, XYZ point)
		{
			if (curves.Count == 0 || !TryGetCurvePoints(curves[0], out var firstCurve))
			{
				return 0.0;
			}
			XYZ closestFirst = GetClosestPointTo(curves[0], point);
			XYZ closestLast = GetClosestPointTo(curves[curves.Count - 1], point);
			XYZ middlePoint = (closestFirst + closestLast) / 2.0;
			XYZ orientation = view.ViewDirection.CrossProduct(firstCurve.Direction.CrossProduct(view.ViewDirection));
			double angle = (point - middlePoint).AngleOnPlaneTo(orientation, view.ViewDirection);
			if (!(angle <= Math.PI / 2.0))
			{
				if (angle <= Math.PI)
				{
					angle = Math.PI - angle;
				}
				else if (angle <= 4.71238898038469)
				{
					angle -= Math.PI;
				}
				else if (angle <= Math.PI * 2.0)
				{
					angle = Math.PI * 2.0 - angle;
				}
			}
			return AllowedAngles.OrderBy((double candidate) => Math.Abs(candidate - angle)).First();
		}

		private List<MEPCurve> ApplyPowerTurn(View view, List<MEPCurve> selectedCurves, XYZ point, double angle)
		{
			if (selectedCurves.Count == 0 || !TryGetCurvePoints(selectedCurves[0], out var firstCurve))
			{
				return selectedCurves;
			}
			Plane viewPlane = Plane.CreateByNormalAndOrigin(-view.ViewDirection, view.Origin);
			XYZ referenceDirection = firstCurve.Direction.CrossProduct(view.ViewDirection);
			XYZ firstPoint = ((firstCurve.Start.DistanceTo(point) >= firstCurve.End.DistanceTo(point)) ? firstCurve.Start : firstCurve.End);
			XYZ secondPoint = ((firstCurve.Start.DistanceTo(point) >= firstCurve.End.DistanceTo(point)) ? firstCurve.End : firstCurve.Start);
			UV uv1 = ProjectInto(viewPlane, firstPoint);
			UV uv2 = ProjectInto(viewPlane, secondPoint);
			UV uv3 = ProjectInto(viewPlane, point);
			double orientationSign = SignedArea(uv1, uv2, uv3);
			List<MEPCurve> turnedCurves = new List<MEPCurve>();
			List<ElementId> selectedIds = new List<ElementId>();
			foreach (MEPCurve sourceCurve in selectedCurves)
			{
				MEPCurve turnedCurve = PowerTurn(view, sourceCurve, point, angle);
				if (turnedCurve != null && TryGetCurvePoints(turnedCurve, out var turnedPoints) && TryGetCurvePoints(sourceCurve, out var sourcePoints))
				{
					XYZ moveDirection = turnedPoints.Direction.CrossProduct(view.ViewDirection).Normalize();
					double moveDistance = Math.Abs((sourcePoints.Start - firstCurve.Start).DotProduct(referenceDirection));
					UV uv4 = ProjectInto(viewPlane, GetClosestPointTo(sourceCurve, point));
					UV uv5 = ProjectInto(viewPlane, point);
					UV uv6 = ProjectInto(viewPlane, point + moveDirection * moveDistance);
					if (SignedArea(uv4, uv5, uv6) * orientationSign < 0.0)
					{
						moveDirection = moveDirection.Negate();
					}
					ElementTransformUtils.MoveElement(Document, turnedCurve.Id, moveDirection * moveDistance);
					selectedIds.Add(turnedCurve.Id);
					turnedCurves.Add(turnedCurve);
				}
			}
			if (turnedCurves.Count > 0)
			{
				UiDocument.Selection.SetElementIds(selectedIds);
				return turnedCurves;
			}
			return selectedCurves;
		}

		private static bool IsAlignedToPoint(View view, XYZ point, IList<MEPCurve> curves)
		{
			if (curves.Count <= 1 || !TryGetCurvePoints(curves[0], out var firstCurve))
			{
				return true;
			}
			XYZ firstNear = ((firstCurve.Start.DistanceTo(point) >= firstCurve.End.DistanceTo(point)) ? firstCurve.End : firstCurve.Start);
			XYZ q = firstCurve.Direction.CrossProduct(view.ViewDirection).Normalize();
			int side = (((point - firstNear).DotProduct(q) >= 0.0) ? 1 : (-1));
			foreach (MEPCurve curve in curves.Skip(1))
			{
				if (TryGetCurvePoints(curve, out var curvePoints))
				{
					XYZ near = ((curvePoints.Start.DistanceTo(point) >= curvePoints.End.DistanceTo(point)) ? curvePoints.End : curvePoints.Start);
					XYZ compareDirection = view.ViewDirection.CrossProduct((near - firstNear).CrossProduct(view.ViewDirection));
					int compareSide = (((point - near).DotProduct(q) >= 0.0) ? 1 : (-1));
					if (!IsParallel(compareDirection, q) || compareSide != side)
					{
						return false;
					}
				}
			}
			return true;
		}

		private static List<MEPCurve> OrderMEPCurveByPoints(View view, List<MEPCurve> curves, XYZ point)
		{
			Plane viewPlane = Plane.CreateByNormalAndOrigin(view.ViewDirection, view.Origin);
			XYZ projectedPoint = ProjectOnto(viewPlane, point);
			return curves.OrderByDescending(delegate(MEPCurve curve)
			{
				if (!TryGetCurvePoints(curve, out var curvePoints))
				{
					return 0.0;
				}
				XYZ point2 = ((curvePoints.Start.DistanceTo(point) > curvePoints.End.DistanceTo(point)) ? curvePoints.End : curvePoints.Start);
				return ProjectOnto(viewPlane, point2).DistanceTo(projectedPoint);
			}).ToList();
		}

		private MEPCurve? PowerTurn(View view, MEPCurve mepCurve, XYZ point, double angle)
		{
			double rotationAngle = Math.PI / 2.0 - angle;
			if (!TryGetCurvePoints(mepCurve, out var sourcePoints))
			{
				return null;
			}
			XYZ nearPoint = ((sourcePoints.Start.DistanceTo(point) >= sourcePoints.End.DistanceTo(point)) ? sourcePoints.End : sourcePoints.Start);
			XYZ farPoint = ((sourcePoints.Start.DistanceTo(point) >= sourcePoints.End.DistanceTo(point)) ? sourcePoints.Start : sourcePoints.End);
			XYZ direction = (nearPoint - farPoint).Normalize();
			Plane byViewPlane = Plane.CreateByNormalAndOrigin(view.ViewDirection, view.Origin);
			UV uvNear = ProjectInto(byViewPlane, nearPoint);
			UV uvFar = ProjectInto(byViewPlane, farPoint);
			UV uvPoint = ProjectInto(byViewPlane, point);
			if (SignedArea(uvNear, uvFar, uvPoint) < 0.0)
			{
				rotationAngle *= -1.0;
			}
			XYZ localNormal = view.ViewDirection.CrossProduct(direction.CrossProduct(view.ViewDirection));
			XYZ planeNormal1 = Transform.CreateRotation(view.ViewDirection, rotationAngle).OfVector(localNormal);
			XYZ lineIntersection1 = GetPlaneLineIntersection(point, planeNormal1, nearPoint, direction);
			if (lineIntersection1.GetLength() >= 50000.0)
			{
				return null;
			}
			AdjustMEPCurveLocation(mepCurve, lineIntersection1, nearPoint);
			XYZ rotatedDirection = Transform.CreateRotation(view.ViewDirection, 0.0 - (Math.PI / 2.0 - rotationAngle)).OfVector(direction);
			XYZ planeNormal2 = rotatedDirection.CrossProduct(view.ViewDirection.CrossProduct(rotatedDirection));
			XYZ lineIntersection2 = GetPlaneLineIntersection(lineIntersection1, planeNormal2, point, view.ViewDirection);
			if (lineIntersection2.GetLength() >= 50000.0)
			{
				return null;
			}
			MEPCurve createdCurve = CreateCurveLike(Document, mepCurve, lineIntersection1, lineIntersection2);
			if (createdCurve == null)
			{
				return null;
			}
			TryConnectMepCurves(mepCurve, lineIntersection1, createdCurve, lineIntersection1);
			Document.Regenerate();
			return createdCurve;
		}

		private void TryConnectMepCurves(MEPCurve first, XYZ firstPoint, MEPCurve second, XYZ secondPoint)
		{
			Connector firstConnector = FindClosestEndConnector(first, firstPoint);
			Connector secondConnector = FindClosestEndConnector(second, secondPoint);
			if (firstConnector == null || secondConnector == null)
			{
				return;
			}
			try
			{
				Document.Create.NewElbowFitting(firstConnector, secondConnector);
			}
			catch
			{
				try
				{
					firstConnector.ConnectTo(secondConnector);
				}
				catch
				{
				}
			}
		}

		private static Connector? FindClosestEndConnector(Element element, XYZ point)
		{
			List<Connector> connectors = ConnectorUtilities.GetConnectors(element);
			if (connectors == null || connectors.Count == 0)
			{
				return null;
			}
			Connector connector = (from c in connectors
				where c.ConnectorType == ConnectorType.End
				orderby c.Origin.DistanceTo(point)
				select c).FirstOrDefault();
			return connector ?? connectors.OrderBy((Connector c) => c.Origin.DistanceTo(point)).FirstOrDefault();
		}

		private static MEPCurve? CreateCurveLike(Document document, MEPCurve template, XYZ start, XYZ end)
		{
			try
			{
				if (template is Pipe pipe)
				{
					ElementId systemTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
					ElementId levelId = pipe.ReferenceLevel?.Id ?? FindNearestLevelId(document, start.Z);
					Pipe newPipe = Pipe.Create(document, systemTypeId, pipe.GetTypeId(), levelId, start, end);
					double diameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
					Parameter targetDiameter = ((Element)newPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
					if (targetDiameter != null && !targetDiameter.IsReadOnly && diameter > 1E-09)
					{
						targetDiameter.Set(diameter);
					}
					return newPipe;
				}
				if (template is Duct duct)
				{
					ElementId systemTypeId2 = ((Element)duct).get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM).AsElementId();
					ElementId levelId2 = duct.ReferenceLevel?.Id ?? FindNearestLevelId(document, start.Z);
					Duct newDuct = Duct.Create(document, systemTypeId2, duct.GetTypeId(), levelId2, start, end);
					CopyIfWritable(((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM), ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM));
					CopyIfWritable(((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM), ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM));
					CopyIfWritable(((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM), ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM));
					return newDuct;
				}
				if (template is CableTray cableTray)
				{
					ElementId levelId3 = ((cableTray.LevelId != ElementId.InvalidElementId) ? cableTray.LevelId : FindNearestLevelId(document, start.Z));
					CableTray newCableTray = CableTray.Create(document, cableTray.GetTypeId(), start, end, levelId3);
					CopyIfWritable(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM), ((Element)newCableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM));
					CopyIfWritable(((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM), ((Element)newCableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM));
					return newCableTray;
				}
				if (template is Conduit conduit)
				{
					ElementId levelId4 = ((conduit.LevelId != ElementId.InvalidElementId) ? conduit.LevelId : FindNearestLevelId(document, start.Z));
					Conduit newConduit = Conduit.Create(document, conduit.GetTypeId(), start, end, levelId4);
					CopyIfWritable(((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM), ((Element)newConduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM));
					return newConduit;
				}
			}
			catch
			{
				return null;
			}
			return null;
		}

		private static void CopyIfWritable(Parameter? source, Parameter? target)
		{
			if (source != null && target != null && !target.IsReadOnly)
			{
				if (source.StorageType == StorageType.Double)
				{
					target.Set(source.AsDouble());
				}
				else if (source.StorageType == StorageType.Integer)
				{
					target.Set(source.AsInteger());
				}
				else if (source.StorageType == StorageType.String)
				{
					target.Set(source.AsString());
				}
				else if (source.StorageType == StorageType.ElementId)
				{
					target.Set(source.AsElementId());
				}
			}
		}

		private static ElementId FindNearestLevelId(Document document, double z)
		{
			return (from Level level in new FilteredElementCollector(document).OfClass(typeof(Level))
				orderby Math.Abs(level.Elevation - z)
				select level).FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
		}

		private static bool TryGetCurvePoints(Element element, out CurvePoints curvePoints)
		{
			curvePoints = default(CurvePoints);
			if (!(element?.Location is LocationCurve locationCurve))
			{
				return false;
			}
			XYZ start = locationCurve.Curve.GetEndPoint(0);
			XYZ end = locationCurve.Curve.GetEndPoint(1);
			XYZ direction = (end - start).Normalize();
			curvePoints = new CurvePoints(start, end, direction);
			return true;
		}

		private static XYZ GetClosestPointTo(MEPCurve mepCurve, XYZ point)
		{
			if (!TryGetCurvePoints(mepCurve, out var curvePoints))
			{
				return point;
			}
			return (curvePoints.Start.DistanceTo(point) > curvePoints.End.DistanceTo(point)) ? curvePoints.End : curvePoints.Start;
		}

		private static void AdjustMEPCurveLocation(MEPCurve mepCurve, XYZ point)
		{
			if (!TryGetCurvePoints(mepCurve, out var curvePoints))
			{
				return;
			}
			Connector startConnector = FindClosestEndConnector(mepCurve, curvePoints.Start);
			Connector endConnector = FindClosestEndConnector(mepCurve, curvePoints.End);
			bool startConnected = startConnector?.IsConnected ?? false;
			bool endConnected = endConnector?.IsConnected ?? false;
			if (mepCurve.Location is LocationCurve locationCurve)
			{
				if ((!startConnected && !endConnected) || (startConnected && endConnected))
				{
					locationCurve.Curve = ((point.DistanceTo(curvePoints.Start) < point.DistanceTo(curvePoints.End)) ? Line.CreateBound(point, curvePoints.End) : Line.CreateBound(curvePoints.Start, point));
				}
				else if (startConnected)
				{
					locationCurve.Curve = Line.CreateBound(curvePoints.Start, point);
				}
				else if (endConnected)
				{
					locationCurve.Curve = Line.CreateBound(point, curvePoints.End);
				}
			}
		}

		private static void AdjustMEPCurveLocation(MEPCurve mepCurve, XYZ point, XYZ referencePoint)
		{
			if (TryGetCurvePoints(mepCurve, out var curvePoints) && mepCurve.Location is LocationCurve locationCurve)
			{
				locationCurve.Curve = ((referencePoint.DistanceTo(curvePoints.Start) < referencePoint.DistanceTo(curvePoints.End)) ? Line.CreateBound(point, curvePoints.End) : Line.CreateBound(curvePoints.Start, point));
			}
		}

		private static XYZ GetPlaneLineIntersection(XYZ planeOrigin, XYZ planeNormal, XYZ lineOrigin, XYZ lineDirection)
		{
			XYZ normal = planeNormal.Normalize();
			double denominator = lineDirection.DotProduct(normal);
			if (Math.Abs(denominator) < 1E-05)
			{
				return new XYZ(-99999.0, -99999.0, -99999.0);
			}
			double t = (planeOrigin - lineOrigin).DotProduct(normal) / denominator;
			return lineOrigin + lineDirection * t;
		}

		private static bool IsParallel(XYZ first, XYZ second)
		{
			XYZ p = first.Normalize();
			XYZ q = second.Normalize();
			return p.IsAlmostEqualTo(q) || p.IsAlmostEqualTo(q.Negate());
		}

		private static XYZ ProjectOnto(Plane plane, XYZ point)
		{
			XYZ originToPoint = point - plane.Origin;
			double distance = plane.Normal.DotProduct(originToPoint);
			return point - distance * plane.Normal;
		}

		private static UV ProjectInto(Plane plane, XYZ point)
		{
			XYZ projected = ProjectOnto(plane, point) - plane.Origin;
			return new UV(projected.DotProduct(plane.XVec), projected.DotProduct(plane.YVec));
		}

		private static double SignedArea(UV p1, UV p2, UV p3)
		{
			return (p1.U - p2.U) * (p1.V + p2.V) + (p2.U - p3.U) * (p2.V + p3.V) + (p3.U - p1.U) * (p3.V + p1.V);
		}
	}

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Runner runner = new Runner(uiDocument, document);
		runner.Run();
	}
}
