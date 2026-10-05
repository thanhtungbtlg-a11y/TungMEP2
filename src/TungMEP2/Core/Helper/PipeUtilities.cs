using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Helper;

public static class PipeUtilities
{
	private const double Gom1E1YV2Eps = 1E-09;

	public static Line? GOM1E1Y(Document doc, Pipe pipeMain, Pipe pipeSub, string target = "l", double delta = 2.0, double kElbow = 1.0)
	{
		try
		{
			Line v2Line = GOM1E1Y_V2(pipeMain, pipeSub, target, delta, kElbow);
			if (v2Line != null)
			{
				return v2Line;
			}
		}
		catch (InvalidOperationException)
		{
		}
		return GOM1E1Y_LegacyScan(pipeMain, pipeSub, target, delta, kElbow);
	}

	public static Pipe CreatePipeFromTemplate(Line line, Pipe templatePipe, double? dnMm = null)
	{
		Document document = templatePipe.Document;
		ElementId pipeTypeId = templatePipe.GetTypeId();
		ElementId systemTypeId = ((Element)templatePipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
		Level level = templatePipe.ReferenceLevel;
		XYZ start = line.GetEndPoint(0);
		XYZ end = line.GetEndPoint(1);
		Pipe newPipe = Pipe.Create(document, systemTypeId, pipeTypeId, level.Id, start, end);
		double diameter = (dnMm.HasValue ? UnitUtils.ConvertToInternalUnits(dnMm.Value, UnitTypeId.Millimeters) : ((Element)templatePipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble());
		((Element)newPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(diameter);
		return newPipe;
	}

	public static FamilyInstance CreateElbow(this Pipe pipe1, Pipe pipe2)
	{
		if (HaveSameDiameter(pipe1, pipe2))
		{
			return CreateElbowDirect(pipe1, pipe2);
		}
		Document document = pipe1.Document;
		using SubTransaction transitionTransaction = new SubTransaction(document);
		transitionTransaction.Start();
		try
		{
			FamilyInstance elbow = CreateElbowWithAutoTransition(pipe1, pipe2);
			document.Regenerate();
			transitionTransaction.Commit();
			return elbow;
		}
		catch
		{
			if (transitionTransaction.GetStatus() == TransactionStatus.Started)
			{
				transitionTransaction.RollBack();
			}
			throw;
		}
	}

	private static FamilyInstance CreateElbowDirect(Pipe pipe1, Pipe pipe2)
	{
		Document document = pipe1.Document;
		LocationCurve location1 = (LocationCurve)pipe1.Location;
		LocationCurve location2 = (LocationCurve)pipe2.Location;
		Line line1 = (Line)location1.Curve;
		Line line2 = (Line)location2.Curve;
		double tolerance = Math.Max(document.Application.ShortCurveTolerance, 1E-06);
		XYZ intersection = ResolveIntersectionForElbow(pipe1, pipe2, line1, line2, tolerance);
		location1.Curve = CreateAdjustedLineAtOpenEnd(pipe1, line1, intersection);
		location2.Curve = CreateAdjustedLineAtOpenEnd(pipe2, line2, intersection);
		document.Regenerate();
		Connector connector1 = ConnectorUtilities.FindClosestConnector(pipe1.ConnectorManager.Connectors, intersection, endOnly: true, unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(pipe1, intersection);
		Connector connector2 = ConnectorUtilities.FindClosestConnector(pipe2.ConnectorManager.Connectors, intersection, endOnly: true, unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(pipe2, intersection);
		FamilyInstance elbow = document.Create.NewElbowFitting(connector1, connector2);
		document.Regenerate();
		return elbow;
	}

	private static FamilyInstance CreateElbowWithAutoTransition(Pipe sourcePipe, Pipe targetPipe)
	{
		Document document = sourcePipe.Document;
		Line sourceLine = (Line)((LocationCurve)sourcePipe.Location).Curve;
		Line targetLine = (Line)((LocationCurve)targetPipe.Location).Curve;
		double tolerance = Math.Max(document.Application.ShortCurveTolerance, 1E-06);
		XYZ intersection = ResolveIntersectionForElbow(sourcePipe, targetPipe, sourceLine, targetLine, tolerance);
		double targetDiameter = GetDiameterInternal(targetPipe);
		TransitionLayout layout = PrepareTransitionLayout(sourcePipe, sourceLine, intersection, targetDiameter);
		double targetDiameterMm = UnitUtils.ConvertFromInternalUnits(targetDiameter, UnitTypeId.Millimeters);

		((LocationCurve)sourcePipe.Location).Curve = ReplaceOpenEnd(
			sourceLine,
			layout.OpenEndIndex,
			layout.SourceTransitionPoint);
		((LocationCurve)targetPipe.Location).Curve = CreateAdjustedLineAtOpenEnd(
			targetPipe,
			targetLine,
			intersection);
		Pipe elbowStub = CreatePipeFromTemplate(
			Line.CreateBound(layout.StubTransitionPoint, intersection),
			sourcePipe,
			targetDiameterMm);
		document.Regenerate();

		FamilyInstance elbow = CreateElbowDirect(elbowStub, targetPipe);
		document.Regenerate();
		Connector sourceConnector = ConnectorUtilities.FindClosestConnector(
			sourcePipe.ConnectorManager.Connectors,
			layout.SourceTransitionPoint,
			endOnly: true,
			unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(sourcePipe, layout.SourceTransitionPoint);
		Connector stubConnector = ConnectorUtilities.FindClosestConnector(
			elbowStub.ConnectorManager.Connectors,
			layout.StubTransitionPoint,
			endOnly: true,
			unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(elbowStub, layout.StubTransitionPoint);
		try
		{
			document.Create.NewTransitionFitting(sourceConnector, stubConnector);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				$"Không tạo được côn chuyển DN {FormatDn(sourcePipe)} → DN {FormatDn(targetPipe)} trước elbow. " +
				"Kiểm tra Transition trong Routing Preferences và size của family.",
				ex);
		}
		document.Regenerate();
		return elbow;
	}

	private static FamilyInstance TeeMasterWithAutoTransition(Pipe mainPipe, Pipe branchPipe)
	{
		Document document = mainPipe.Document;
		using SubTransaction transitionTransaction = new SubTransaction(document);
		transitionTransaction.Start();
		try
		{
			Line mainLine = (Line)((LocationCurve)mainPipe.Location).Curve;
			Line branchLine = (Line)((LocationCurve)branchPipe.Location).Curve;
			XYZ intersection = GetUnboundIntersection(NormalizeLineByZ(mainLine), NormalizeLineByZ(branchLine));
			double mainDiameter = GetDiameterInternal(mainPipe);
			TransitionLayout layout = PrepareTransitionLayout(branchPipe, branchLine, intersection, mainDiameter);
			double mainDiameterMm = UnitUtils.ConvertFromInternalUnits(mainDiameter, UnitTypeId.Millimeters);

			((LocationCurve)branchPipe.Location).Curve = ReplaceOpenEnd(
				(Line)((LocationCurve)branchPipe.Location).Curve,
				layout.OpenEndIndex,
				layout.SourceTransitionPoint);
			Pipe teeStub = CreatePipeFromTemplate(
				Line.CreateBound(layout.StubTransitionPoint, intersection),
				branchPipe,
				mainDiameterMm);
			document.Regenerate();

			FamilyInstance tee = TeeMasterDirect(mainPipe, teeStub);
			document.Regenerate();
			Connector branchConnector = ConnectorUtilities.FindClosestConnector(
				branchPipe.ConnectorManager.Connectors,
				layout.SourceTransitionPoint,
				endOnly: true,
				unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(branchPipe, layout.SourceTransitionPoint);
			Connector stubConnector = ConnectorUtilities.FindClosestConnector(
				teeStub.ConnectorManager.Connectors,
				layout.StubTransitionPoint,
				endOnly: true,
				unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(teeStub, layout.StubTransitionPoint);
			try
			{
				document.Create.NewTransitionFitting(branchConnector, stubConnector);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					$"Không tạo được côn chuyển DN {FormatDn(branchPipe)} → DN {FormatDn(mainPipe)} trước Tee/Y. " +
					"Kiểm tra Transition trong Routing Preferences và size của family.",
					ex);
			}
			document.Regenerate();
			transitionTransaction.Commit();
			return tee;
		}
		catch
		{
			if (transitionTransaction.GetStatus() == TransactionStatus.Started)
			{
				transitionTransaction.RollBack();
			}
			throw;
		}
	}

	public static Line? GOM1E1Y_V2(Pipe pipeMain, Pipe pipeSub, string target = "l", double delta = 2.0, double kElbow = 1.0)
	{
		Line lineMain = NormalizeLineByZ((Line)((LocationCurve)pipeMain.Location).Curve);
		Line lineSub = NormalizeLineByZ((Line)((LocationCurve)pipeSub.Location).Curve);
		XYZ directionMain = lineMain.Direction.Normalize();
		XYZ directionSub = lineSub.Direction.Normalize();
		XYZ intersect = null;
		try
		{
			Plane plane = Plane.CreateByNormalAndOrigin(directionSub.CrossProduct(directionMain), lineSub.GetEndPoint(1));
			Line mainOnPlane = (Line)PullOntoPlane(lineMain, plane);
			Line subOnPlane = (Line)PullOntoPlane(lineSub, plane);
			Line mainExtended = ExtendLine(mainOnPlane, 10000.0);
			Line subExtended = ExtendLine(subOnPlane, 10000.0);
			intersect = TryFindIntersection(mainExtended, subExtended);
		}
		catch
		{
		}
		if (intersect != null)
		{
			XYZ sub0 = lineSub.GetEndPoint(0);
			XYZ sub1 = lineSub.GetEndPoint(1);
			lineSub = Line.CreateBound(sub0, sub1 + directionSub.Multiply(sub1.DistanceTo(intersect)));
			XYZ main1 = lineMain.GetEndPoint(1);
			lineMain = Line.CreateBound(main1 + directionMain.Negate().Multiply(main1.DistanceTo(intersect)), main1);
		}
		XYZ pointMain = lineMain.GetEndPoint(1);
		XYZ pointSub = lineSub.GetEndPoint(1);
		double elbowLength = GetElbowLengthInternal(pipeSub);
		double yLengthInternal = GetYLengthInternal(pipeMain, pipeSub, target);
		double lMin = elbowLength * kElbow + yLengthInternal;
		double[] a = new double[3] { pointMain.X, pointMain.Y, pointMain.Z };
		double[] b = new double[3] { pointSub.X, pointSub.Y, pointSub.Z };
		double[] u = new double[3] { directionMain.X, directionMain.Y, directionMain.Z };
		double[] v = new double[3] { directionSub.X, directionSub.Y, directionSub.Z };
		if (!TrySolveGom1E1YV2(a, b, u, v, lMin, lineMain.Length, lineSub.Length, delta, out double[] p1, out double[] p2) || p1 == null || p2 == null)
		{
			return null;
		}
		return Line.CreateBound(new XYZ(p1[0], p1[1], p1[2]), new XYZ(p2[0], p2[1], p2[2]));
	}

	private static Line? GOM1E1Y_LegacyScan(Pipe pipeMain, Pipe pipeSub, string target, double deltaDeg, double kElbow)
	{
		Line lineMain = NormalizeLineByZ((Line)((LocationCurve)pipeMain.Location).Curve);
		Line lineSub = NormalizeLineByZ((Line)((LocationCurve)pipeSub.Location).Curve);
		XYZ directionMain = lineMain.Direction.Normalize();
		XYZ directionSub = lineSub.Direction.Normalize();
		XYZ intersect = null;
		try
		{
			Plane plane = Plane.CreateByNormalAndOrigin(directionSub.CrossProduct(directionMain), lineSub.GetEndPoint(1));
			Line mainOnPlane = (Line)PullOntoPlane(lineMain, plane);
			Line subOnPlane = (Line)PullOntoPlane(lineSub, plane);
			Line mainExtended = ExtendLine(mainOnPlane, 10000.0);
			Line subExtended = ExtendLine(subOnPlane, 10000.0);
			intersect = TryFindIntersection(mainExtended, subExtended);
		}
		catch
		{
		}
		if (intersect != null)
		{
			XYZ sub0 = lineSub.GetEndPoint(0);
			XYZ sub1 = lineSub.GetEndPoint(1);
			lineSub = Line.CreateBound(sub0, sub1 + directionSub.Multiply(sub1.DistanceTo(intersect)));
			XYZ main1 = lineMain.GetEndPoint(1);
			lineMain = Line.CreateBound(main1 + directionMain.Negate().Multiply(main1.DistanceTo(intersect)), main1);
		}
		double lMin;
		try
		{
			double elbowLength = GetElbowLengthInternal(pipeSub);
			double yLength = GetYLengthInternal(pipeMain, pipeSub, target);
			lMin = elbowLength * kElbow + yLength;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		XYZ pointMain = lineMain.GetEndPoint(1);
		XYZ pointSub = lineSub.GetEndPoint(1);
		for (double lSub = 0.0; lSub < lineSub.Length; lSub += 0.1)
		{
			for (double lMain = 0.0; lMain < lineMain.Length; lMain += 0.1)
			{
				XYZ pointP1 = pointMain + directionMain.Negate().Multiply(lMain);
				XYZ pointP2 = pointSub + directionSub.Negate().Multiply(lSub);
				if (pointP1.DistanceTo(pointP2) > lMin)
				{
					XYZ vectorP1P2 = pointP2 - pointP1;
					double angleWithMain = vectorP1P2.AngleTo(directionMain) * (180.0 / Math.PI);
					double angleWithSub = vectorP1P2.AngleTo(directionSub) * (180.0 / Math.PI);
					if (Math.Abs(angleWithMain - 135.0) < deltaDeg && Math.Abs(angleWithSub - 135.0) < deltaDeg)
					{
						return Line.CreateBound(pointP1, pointP2);
					}
				}
			}
		}
		return null;
	}

	private static bool TrySolveGom1E1YV2(double[] a, double[] b, double[] u, double[] v, double lMin, double maxMain, double maxSub, double deltaDeg, out double[]? p1, out double[]? p2)
	{
		p1 = null;
		p2 = null;
		if (!IsVector3(a) || !IsVector3(b) || !IsVector3(u) || !IsVector3(v))
		{
			return false;
		}
		if (Norm(u) < 1E-09 || Norm(v) < 1E-09)
		{
			return false;
		}
		double[] uN = Normalize(u);
		double[] vN = Normalize(v);
		double c = Dot(uN, vN);
		double denom = 1.0 - c;
		if (Math.Abs(denom) < 1E-09)
		{
			return false;
		}
		double[] d = Subtract(b, a);
		double sPlusT = (0.0 - Dot(d, Subtract(uN, vN))) / denom;
		double[] p3 = Add(uN, vN);
		double[] q = Subtract(d, Multiply(vN, sPlusT));
		double pu = Dot(p3, uN);
		double pp = Dot(p3, p3);
		double qu = Dot(q, uN);
		double qp = Dot(q, p3);
		double qq = Dot(q, q);
		double a2 = 2.0 * pu * pu - pp;
		double b2 = 4.0 * qu * pu - 2.0 * qp;
		double c2 = 2.0 * qu * qu - qq;
		double? root1 = null;
		double? root2 = null;
		if (Math.Abs(a2) < 1E-09)
		{
			if (Math.Abs(b2) < 1E-09)
			{
				if (Math.Abs(c2) > 1E-09)
				{
					return false;
				}
				double sMin = Math.Max(0.0, sPlusT - maxSub);
				double sMax = Math.Min(maxMain, sPlusT);
				if (sMin > sMax)
				{
					return false;
				}
				double ppSafe = Dot(p3, p3);
				if (ppSafe < 1E-09)
				{
					return false;
				}
				double sStar = (0.0 - Dot(q, p3)) / ppSafe;
				double sClamped = Math.Max(sMin, Math.Min(sMax, sStar));
				root1 = sClamped;
				root2 = ((sMin == sMax) ? ((double?)null) : new double?(sMin));
				if (root2.HasValue && Math.Abs(root2.Value - root1.Value) < 1E-09)
				{
					root2 = sMax;
				}
			}
			else
			{
				root1 = (0.0 - c2) / b2;
			}
		}
		else
		{
			double delta = b2 * b2 - 4.0 * a2 * c2;
			if (delta < 0.0)
			{
				return false;
			}
			double sqrtDelta = Math.Sqrt(delta);
			root1 = (0.0 - b2 + sqrtDelta) / (2.0 * a2);
			root2 = (0.0 - b2 - sqrtDelta) / (2.0 * a2);
		}
		bool found = false;
		double bestLength = double.MaxValue;
		double[] bestP1 = null;
		double[] bestP2 = null;
		Evaluate(root1);
		Evaluate(root2);
		if (!found)
		{
			return false;
		}
		p1 = bestP1;
		p2 = bestP2;
		return true;
		void Evaluate(double? num)
		{
			if (num.HasValue)
			{
				double s = num.Value;
				double t = sPlusT - s;
				if (InRange(s, 0.0, maxMain) && InRange(t, 0.0, maxSub))
				{
					double[] candidateP1 = Subtract(a, Multiply(uN, s));
					double[] candidateP2 = Subtract(b, Multiply(vN, t));
					double[] w = Subtract(candidateP2, candidateP1);
					double wLength = Norm(w);
					if (!(wLength <= lMin + 1E-09))
					{
						double angleU = AngleDeg(w, uN);
						double angleV = AngleDeg(w, vN);
						if (!(Math.Abs(angleU - 135.0) > deltaDeg) && !(Math.Abs(angleV - 135.0) > deltaDeg) && !(Dot(w, uN) >= 0.0) && !(Dot(w, vN) >= 0.0) && !(wLength >= bestLength))
						{
							found = true;
							bestLength = wLength;
							bestP1 = candidateP1;
							bestP2 = candidateP2;
						}
					}
				}
			}
		}
	}

	private static bool IsVector3(double[] vector)
	{
		return vector.Length == 3;
	}

	private static bool InRange(double value, double min, double max)
	{
		return value >= min - 1E-09 && value <= max + 1E-09;
	}

	private static double[] Add(double[] a, double[] b)
	{
		return new double[3]
		{
			a[0] + b[0],
			a[1] + b[1],
			a[2] + b[2]
		};
	}

	private static double[] Subtract(double[] a, double[] b)
	{
		return new double[3]
		{
			a[0] - b[0],
			a[1] - b[1],
			a[2] - b[2]
		};
	}

	private static double[] Multiply(double[] a, double scale)
	{
		return new double[3]
		{
			a[0] * scale,
			a[1] * scale,
			a[2] * scale
		};
	}

	private static double Dot(double[] a, double[] b)
	{
		return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
	}

	private static double Norm(double[] a)
	{
		return Math.Sqrt(Dot(a, a));
	}

	private static double[] Normalize(double[] a)
	{
		double length = Norm(a);
		return (length < 1E-09) ? a : Multiply(a, 1.0 / length);
	}

	private static double AngleDeg(double[] a, double[] b)
	{
		double denom = Norm(a) * Norm(b);
		if (denom < 1E-09)
		{
			return 0.0;
		}
		double cos = Dot(a, b) / denom;
		cos = Math.Max(-1.0, Math.Min(1.0, cos));
		return Math.Acos(cos) * (180.0 / Math.PI);
	}

	public static List<Line>? GOM2E1Yv1(Pipe pipeMain, Pipe pipeSub)
	{
		Line lineMain = NormalizeLineByZ((Line)((LocationCurve)pipeMain.Location).Curve);
		Line lineSub = NormalizeLineByZ((Line)((LocationCurve)pipeSub.Location).Curve);
		XYZ mainDirection = lineMain.Direction.Normalize();
		XYZ subDirection = lineSub.Direction.Normalize();
		XYZ pipeMainVectorXY = new XYZ(mainDirection.X, mainDirection.Y, 0.0);
		XYZ pipeSubVectorXY = new XYZ(subDirection.X, subDirection.Y, 0.0);
		if (pipeMainVectorXY.GetLength() < 1E-09 || pipeSubVectorXY.GetLength() < 1E-09)
		{
			return null;
		}
		XYZ mainOriginXY = new XYZ(lineMain.GetEndPoint(0).X, lineMain.GetEndPoint(0).Y, 0.0);
		XYZ subOriginXY = new XYZ(lineSub.GetEndPoint(0).X, lineSub.GetEndPoint(0).Y, 0.0);
		Line lineMainXY = Line.CreateUnbound(mainOriginXY, pipeMainVectorXY.Normalize());
		Line lineSubXY = Line.CreateUnbound(subOriginXY, pipeSubVectorXY.Normalize());
		IntersectionResultArray intersections;
		SetComparisonResult xyResult = lineMainXY.Intersect(lineSubXY, out intersections);
		if (xyResult != SetComparisonResult.Overlap || intersections == null || intersections.Size == 0)
		{
			return null;
		}
		XYZ p1Prime = intersections.get_Item(0).XYZPoint;
		XYZ planeOrigin = new XYZ(p1Prime.X, p1Prime.Y, 0.0);
		Plane planeSub = Plane.CreateByOriginAndBasis(planeOrigin, pipeSubVectorXY.Normalize(), XYZ.BasisZ);
		Line mainUnbound = Line.CreateUnbound(lineMain.GetEndPoint(0), mainDirection);
		double denom = planeSub.Normal.DotProduct(mainUnbound.Direction);
		if (Math.Abs(denom) < 1E-09)
		{
			return null;
		}
		double distance = planeSub.Normal.DotProduct(planeSub.Origin - mainUnbound.Origin) / denom;
		XYZ point1 = mainUnbound.Origin + mainUnbound.Direction.Multiply(distance);
		XYZ subStart = lineSub.GetEndPoint(0);
		XYZ w = subStart - point1;
		double wDotV = w.DotProduct(subDirection);
		double wNorm2 = w.DotProduct(w);
		double discriminant = wNorm2 - wDotV * wDotV;
		if (discriminant < -1E-09)
		{
			return null;
		}
		double sqrt = Math.Sqrt(Math.Max(0.0, discriminant));
		double t1 = 0.0 - wDotV + sqrt;
		double t2 = 0.0 - wDotV - sqrt;
		XYZ point2 = null;
		double bestLength = double.MaxValue;
		Evaluate(t1);
		Evaluate(t2);
		if (point2 == null)
		{
			return null;
		}
		double l1 = GetElbowLengthInternal(pipeSub);
		double l2 = GetYLengthInternal(pipeMain, pipeSub, "l");
		double l3 = (l1 + l2) * 1.1;
		double d = l3 * Math.Sqrt(2.0);
		XYZ direction12 = (point2 - point1).Normalize();
		XYZ point3 = point1 + mainDirection.Multiply(d);
		XYZ point4 = point1 + direction12.Multiply(d);
		Line line12 = Line.CreateBound(point1, point2);
		Line line24 = Line.CreateBound(point2, point4);
		Line line43 = Line.CreateBound(point4, point3);
		return new List<Line> { line12, line24, line43 };
		void Evaluate(double num)
		{
			if (!(num < 0.0))
			{
				XYZ candidate = subStart + subDirection.Multiply(num);
				XYZ vector21 = candidate - point1;
				double length = vector21.GetLength();
				if (!(length < 1E-09))
				{
					double angle = UnitUtils.ConvertFromInternalUnits(vector21.AngleTo(subDirection), UnitTypeId.Degrees);
					if (!(Math.Abs(angle - 135.0) > 0.0001) && !(length >= bestLength))
					{
						bestLength = length;
						point2 = candidate;
					}
				}
			}
		}
	}

	public static List<Line>? GOM2E1Yv2(Pipe pipeMain, Pipe pipeSub)
	{
		Line lineMain = NormalizeLineByZ((Line)((LocationCurve)pipeMain.Location).Curve);
		Line lineSub = NormalizeLineByZ((Line)((LocationCurve)pipeSub.Location).Curve);
		XYZ mainDirection = lineMain.Direction.Normalize();
		XYZ subDirection = lineSub.Direction.Normalize();
		XYZ pipeMainVectorXY = new XYZ(mainDirection.X, mainDirection.Y, 0.0);
		XYZ pipeSubVectorXY = new XYZ(subDirection.X, subDirection.Y, 0.0);
		if (pipeMainVectorXY.GetLength() < 1E-09 || pipeSubVectorXY.GetLength() < 1E-09)
		{
			return null;
		}
		XYZ mainOriginXY = new XYZ(lineMain.GetEndPoint(0).X, lineMain.GetEndPoint(0).Y, 0.0);
		XYZ subOriginXY = new XYZ(lineSub.GetEndPoint(0).X, lineSub.GetEndPoint(0).Y, 0.0);
		Line lineMainXY = Line.CreateUnbound(mainOriginXY, pipeMainVectorXY.Normalize());
		Line lineSubXY = Line.CreateUnbound(subOriginXY, pipeSubVectorXY.Normalize());
		IntersectionResultArray intersections;
		SetComparisonResult xyResult = lineMainXY.Intersect(lineSubXY, out intersections);
		if (xyResult != SetComparisonResult.Overlap || intersections == null || intersections.Size == 0)
		{
			return null;
		}
		XYZ p1Prime = intersections.get_Item(0).XYZPoint;
		XYZ p1PrimeOxy = new XYZ(p1Prime.X, p1Prime.Y, 0.0);
		Plane planeSub = Plane.CreateByOriginAndBasis(p1PrimeOxy, pipeSubVectorXY.Normalize(), XYZ.BasisZ);
		Line mainUnbound = Line.CreateUnbound(lineMain.GetEndPoint(0), mainDirection);
		XYZ point1 = IntersectLineWithPlane(mainUnbound, planeSub, 1E-09);
		if (point1 == null)
		{
			return null;
		}
		Plane pla = Plane.CreateByNormalAndOrigin(pipeSubVectorXY.Normalize(), p1PrimeOxy);
		Line subUnbound = Line.CreateUnbound(lineSub.GetEndPoint(0), subDirection);
		XYZ point4 = IntersectLineWithPlane(subUnbound, pla, 1E-09);
		if (point4 == null)
		{
			return null;
		}
		XYZ w = point4 - point1;
		double a = mainDirection.DotProduct(w);
		double perpSquared = w.DotProduct(w) - a * a;
		if (perpSquared < -1E-09)
		{
			return null;
		}
		double sqrt = Math.Sqrt(Math.Max(0.0, perpSquared));
		double s1 = a + sqrt;
		double s2 = a - sqrt;
		XYZ point5 = null;
		double bestS = double.MaxValue;
		EvaluatePoint(s1);
		EvaluatePoint(s2);
		if (point5 == null)
		{
			return null;
		}
		double l1 = GetElbowLengthInternal(pipeSub);
		double l2 = l1 * 1.05;
		double d = l2 * Math.Sqrt(2.0);
		XYZ point6 = point4 - subDirection.Multiply(d);
		XYZ direction43 = (point5 - point4).Normalize();
		XYZ point7 = point4 + direction43.Multiply(d);
		Line line56 = Line.CreateBound(point6, point7);
		Line line63 = Line.CreateBound(point7, point5);
		return new List<Line> { line56, line63 };
		void EvaluatePoint(double num)
		{
			if (!(num < 0.0))
			{
				XYZ candidate = point1 + mainDirection.Multiply(num);
				XYZ vector43 = candidate - point4;
				double length = vector43.GetLength();
				if (!(length < 1E-09))
				{
					double angle = UnitUtils.ConvertFromInternalUnits(vector43.AngleTo(mainDirection), UnitTypeId.Degrees);
					if (!(Math.Abs(angle - 45.0) > 0.0001) && !(num >= bestS))
					{
						bestS = num;
						point5 = candidate;
					}
				}
			}
		}
		static XYZ? IntersectLineWithPlane(Line line64, Plane plane, double tolerance)
		{
			double denominator = plane.Normal.DotProduct(line64.Direction);
			if (Math.Abs(denominator) < tolerance)
			{
				return null;
			}
			double distance = plane.Normal.DotProduct(plane.Origin - line64.Origin) / denominator;
			return line64.Origin + line64.Direction.Multiply(distance);
		}
	}

	public static FamilyInstance TeeMaster(Pipe mainPipe, Pipe branchPipe)
	{
		if (HaveSameDiameter(mainPipe, branchPipe))
		{
			return TeeMasterDirect(mainPipe, branchPipe);
		}
		try
		{
			// Ưu tiên reducing tee/Y nếu family trong Routing Preferences hỗ trợ.
			return TeeMasterDirect(mainPipe, branchPipe);
		}
		catch
		{
			// Nếu reducing tee/Y không có size phù hợp, tạo tee cùng DN ống
			// chính và tự chèn transition trên nhánh.
			return TeeMasterWithAutoTransition(mainPipe, branchPipe);
		}
	}

	private static FamilyInstance TeeMasterDirect(Pipe mainPipe, Pipe branchPipe)
	{
		Document document = mainPipe.Document;
		using SubTransaction subTransaction = new SubTransaction(document);
		subTransaction.Start();
		try
		{
			Line mainLocation = NormalizeLineByZ((Line)((LocationCurve)mainPipe.Location).Curve);
			Line subLocation = NormalizeLineByZ((Line)((LocationCurve)branchPipe.Location).Curve);
			XYZ directionMain = mainLocation.Direction.Normalize();
			XYZ directionSub = subLocation.Direction.Normalize();
			XYZ normal = directionSub.CrossProduct(directionMain);
			if (normal.GetLength() < 1E-09)
			{
				throw new InvalidOperationException("Main pipe and branch pipe are parallel.");
			}
			XYZ findIntersect = GetUnboundIntersection(mainLocation, subLocation);
			XYZ crossProduct = normal.Normalize();
			XYZ crossProduct2 = directionMain.CrossProduct(crossProduct);
			Plane plane = Plane.CreateByOriginAndBasis(findIntersect, crossProduct, crossProduct2);
			Curve ontoPlane = PullOntoPlane((Line)((LocationCurve)branchPipe.Location).Curve, plane);
			ElementId systemTypeId = branchPipe.MEPSystem?.GetTypeId() ?? ((Element)branchPipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
			if (systemTypeId == ElementId.InvalidElementId)
			{
				throw new InvalidOperationException("Cannot determine branch pipe system type.");
			}
			Pipe pipeDem = Pipe.Create(document, systemTypeId, branchPipe.GetTypeId(), branchPipe.LevelId, ontoPlane.GetEndPoint(0), ontoPlane.GetEndPoint(1));
			((Element)pipeDem).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(((Element)branchPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble());
			ElementId newPipeId = PlumbingUtils.BreakCurve(document, mainPipe.Id, findIntersect);
			Pipe newPipe = (Pipe)document.GetElement(newPipeId);
			(Connector, Connector, Connector) connectors = GetClosestConnectors(mainPipe, newPipe, pipeDem);
			FamilyInstance tee = document.Create.NewTeeFitting(connectors.Item1, connectors.Item2, connectors.Item3);
			document.Delete(pipeDem.Id);
			ConnectorSet cnSet = tee.MEPModel.ConnectorManager.Connectors;
			Connector cTee = null;
			foreach (Connector c in cnSet)
			{
				if (!c.IsConnected)
				{
					cTee = c;
					break;
				}
			}
			if (cTee != null)
			{
				Parameter angleParam = FindWritableAngleParameter(tee);
				if (angleParam != null)
				{
					double d = UnitUtils.ConvertFromInternalUnits(cTee.CoordinateSystem.BasisZ.AngleTo(directionSub), UnitTypeId.Degrees);
					double deltaAngle = ((d < 67.5) ? Math.Abs(45.0 - d) : Math.Abs(135.0 - d));
					List<double> cases = ((deltaAngle < 0.01) ? new List<double> { 45.0, 135.0 } : new List<double>
					{
						45.0 + deltaAngle,
						45.0 - deltaAngle,
						135.0 + deltaAngle,
						135.0 - deltaAngle
					});
					foreach (double value in cases)
					{
						angleParam.Set(UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Degrees));
						document.Regenerate();
						double sin = Math.Abs(Math.Sin(cTee.CoordinateSystem.BasisZ.AngleTo(directionSub)));
						if (Math.Round(sin, 3) == 0.0)
						{
							break;
						}
					}
				}
				Connector cSub = ConnectorUtilities.FindClosestConnector(branchPipe.ConnectorManager.Connectors, cTee.Origin, endOnly: true, unconnectedOnly: true) ?? ConnectorUtilities.GetClosestConnector(branchPipe, cTee.Origin);
				cSub.ConnectTo(cTee);
				ElementTransformUtils.MoveElement(document, tee.Id, XYZ.BasisZ.Multiply(0.0001));
				ElementTransformUtils.MoveElement(document, tee.Id, XYZ.BasisZ.Negate().Multiply(0.0001));
			}
			document.Regenerate();
			subTransaction.Commit();
			return tee;
		}
		catch
		{
			if (subTransaction.GetStatus() == TransactionStatus.Started)
			{
				subTransaction.RollBack();
			}
			throw;
		}
	}

	public static List<ElementId> Elbow90To45(FamilyInstance elbow90)
	{
		Document document = elbow90.Document;
		(Pipe, XYZ, Pipe, XYZ) pipes = GetConnectedPipes(elbow90);
		Pipe pipe1 = pipes.Item1;
		Pipe pipe2 = pipes.Item3;
		LocationCurve location1 = (LocationCurve)pipe1.Location;
		LocationCurve location2 = (LocationCurve)pipe2.Location;
		Line line1 = (Line)location1.Curve;
		Line line2 = (Line)location2.Curve;
		XYZ intersect2Line = line1.Intersect2Line(line2);
		List<(Pipe, Connector)> connectorPairs = GetElbowPipeConnectorPairs(elbow90);
		(Pipe, Connector) pair1 = connectorPairs.Single<(Pipe, Connector)>(((Pipe pipe, Connector elbowConnector) x) => x.pipe.Id == pipe1.Id);
		(Pipe, Connector) pair2 = connectorPairs.Single<(Pipe, Connector)>(((Pipe pipe, Connector elbowConnector) x) => x.pipe.Id == pipe2.Id);
		double intersectionTolerance = Math.Max(document.Application.ShortCurveTolerance, 1E-06);
		XYZ a = ((IsPointOnUnboundLine(line1, intersect2Line, intersectionTolerance) && IsPointOnUnboundLine(line2, intersect2Line, intersectionTolerance)) ? intersect2Line : GetConnectorInverseIntersection(pair1.Item2, pair2.Item2, document.Application.ShortCurveTolerance, line1, line2));
		XYZ v1 = pair1.Item2.CoordinateSystem.BasisZ.Normalize();
		XYZ v2 = pair2.Item2.CoordinateSystem.BasisZ.Normalize();
		if (v1.DotProduct(pair1.Item2.Origin - a) < 0.0)
		{
			v1 = v1.Negate();
		}
		if (v2.DotProduct(pair2.Item2.Origin - a) < 0.0)
		{
			v2 = v2.Negate();
		}
		double l = GetElbowLengthInternal(pipe1) * 2.0;
		double d = l / Math.Sqrt(2.0) * 1.1;
		XYZ b = a + v1.Multiply(d);
		XYZ c = a + v2.Multiply(d);
		document.Delete(elbow90.Id);
		location1.Curve = CreateAdjustedLine(line1, b);
		location2.Curve = CreateAdjustedLine(line2, c);
		document.Regenerate();
		Pipe pipeBc = CreatePipeFromTemplate(Line.CreateBound(b, c), pipe1);
		document.Regenerate();
		FamilyInstance elbow91 = TryCreateElbowWithoutThrow(document, pipe1, pipeBc);
		FamilyInstance elbow92 = TryCreateElbowWithoutThrow(document, pipeBc, pipe2);
		document.Regenerate();
		List<ElementId> createdElementIds = new List<ElementId>();
		if (elbow91 != null)
		{
			createdElementIds.Add(elbow91.Id);
		}
		createdElementIds.Add(pipeBc.Id);
		if (elbow92 != null)
		{
			createdElementIds.Add(elbow92.Id);
		}
		return createdElementIds;
	}

	private static FamilyInstance? TryCreateElbowWithoutThrow(Document document, Pipe pipe1, Pipe pipe2)
	{
		using SubTransaction subTransaction = new SubTransaction(document);
		subTransaction.Start();
		try
		{
			FamilyInstance elbow = pipe1.CreateElbow(pipe2);
			subTransaction.Commit();
			return elbow;
		}
		catch (Exception)
		{
			if (subTransaction.GetStatus() == TransactionStatus.Started)
			{
				subTransaction.RollBack();
			}
			return null;
		}
	}

	private static List<(Pipe pipe, Connector elbowConnector)> GetElbowPipeConnectorPairs(FamilyInstance elbow)
	{
		List<(Pipe, Connector)> result = new List<(Pipe, Connector)>();
		HashSet<ElementId> pipeIds = new HashSet<ElementId>();
		foreach (Connector elbowConnector in elbow.MEPModel.ConnectorManager.Connectors)
		{
			if (!elbowConnector.IsConnected)
			{
				continue;
			}
			foreach (Connector refConnector in elbowConnector.AllRefs)
			{
				if (refConnector.Owner.Id == elbow.Id || !(refConnector.Owner is Pipe pipe))
				{
					continue;
				}
				if (pipeIds.Add(pipe.Id))
				{
					result.Add((pipe, elbowConnector));
				}
				break;
			}
			if (result.Count != 2)
			{
				continue;
			}
			break;
		}
		if (result.Count != 2)
		{
			throw new InvalidOperationException("Elbow must connect exactly two pipes.");
		}
		return result;
	}

	private static XYZ GetConnectorInverseIntersection(Connector connector1, Connector connector2, double tolerance, Line fallbackLine1, Line fallbackLine2)
	{
		XYZ direction1 = connector1.CoordinateSystem.BasisZ.Normalize().Negate();
		XYZ direction2 = connector2.CoordinateSystem.BasisZ.Normalize().Negate();
		Line inverseLine1 = Line.CreateUnbound(connector1.Origin, direction1);
		Line inverseLine2 = Line.CreateUnbound(connector2.Origin, direction2);
		XYZ directIntersection = TryFindIntersection(inverseLine1, inverseLine2);
		if (directIntersection != null)
		{
			return directIntersection;
		}
		if (TryGetClosestPointsMidpoint(inverseLine1, inverseLine2, tolerance, out XYZ midpoint))
		{
			return midpoint;
		}
		return GetUnboundIntersection(fallbackLine1, fallbackLine2);
	}

	private static bool IsPointOnUnboundLine(Line line, XYZ point, double tolerance)
	{
		XYZ direction = line.Direction;
		if (direction.GetLength() < 1E-09)
		{
			return false;
		}
		XYZ fromStart = point - line.GetEndPoint(0);
		double distance = fromStart.CrossProduct(direction.Normalize()).GetLength();
		return distance <= Math.Max(tolerance, 1E-06);
	}

	private static bool TryGetClosestPointsMidpoint(Line line1, Line line2, double tolerance, out XYZ midpoint)
	{
		midpoint = XYZ.Zero;
		XYZ p1 = line1.Origin;
		XYZ p2 = line2.Origin;
		XYZ d1 = line1.Direction.Normalize();
		XYZ d2 = line2.Direction.Normalize();
		XYZ w0 = p1 - p2;
		double a = d1.DotProduct(d1);
		double b = d1.DotProduct(d2);
		double c = d2.DotProduct(d2);
		double d3 = d1.DotProduct(w0);
		double e = d2.DotProduct(w0);
		double denom = a * c - b * b;
		if (Math.Abs(denom) < 1E-09)
		{
			return false;
		}
		double s = (b * e - c * d3) / denom;
		double t = (a * e - b * d3) / denom;
		XYZ cp1 = p1 + d1.Multiply(s);
		XYZ cp2 = p2 + d2.Multiply(t);
		if (cp1.DistanceTo(cp2) > Math.Max(tolerance, 1E-06))
		{
			return false;
		}
		midpoint = (cp1 + cp2).Multiply(0.5);
		return true;
	}

	public static FamilyInstance Elbow45To90(Pipe middlePipe, FamilyInstance fitting1, FamilyInstance fitting2)
	{
		Document document = middlePipe.Document;
		List<Pipe> pipes1 = GetConnectedPipesFromFitting(fitting1);
		List<Pipe> pipes2 = GetConnectedPipesFromFitting(fitting2);
		Pipe main1 = pipes1.Single((Pipe p) => p.Id != middlePipe.Id);
		Pipe main2 = pipes2.Single((Pipe p) => p.Id != middlePipe.Id);
		document.Delete(middlePipe.Id);
		document.Delete(fitting1.Id);
		document.Delete(fitting2.Id);
		document.Regenerate();
		return main1.CreateElbow(main2);
	}

	private static Line CreateAdjustedLine(Line line, XYZ intersection)
	{
		XYZ end0 = line.GetEndPoint(0);
		XYZ end1 = line.GetEndPoint(1);
		double dist0 = end0.DistanceTo(intersection);
		double dist1 = end1.DistanceTo(intersection);
		return (dist0 <= dist1) ? Line.CreateBound(intersection, end1) : Line.CreateBound(end0, intersection);
	}

	private sealed class TransitionLayout
	{
		public int OpenEndIndex { get; set; }

		public XYZ SourceTransitionPoint { get; set; }

		public XYZ StubTransitionPoint { get; set; }
	}

	private static TransitionLayout PrepareTransitionLayout(
		Pipe sourcePipe,
		Line sourceLine,
		XYZ intersection,
		double otherDiameter)
	{
		Document document = sourcePipe.Document;
		Connector openEnd = ConnectorUtilities.FindClosestConnector(
			sourcePipe.ConnectorManager.Connectors,
			intersection,
			endOnly: true,
			unconnectedOnly: true);
		if (openEnd == null)
		{
			throw new InvalidOperationException(
				$"Ống DN {FormatDn(sourcePipe)} cần một đầu trống để chèn côn tự động.");
		}

		XYZ end0 = sourceLine.GetEndPoint(0);
		XYZ end1 = sourceLine.GetEndPoint(1);
		int openEndIndex = end0.DistanceTo(openEnd.Origin) <= end1.DistanceTo(openEnd.Origin) ? 0 : 1;
		XYZ farEnd = sourceLine.GetEndPoint(1 - openEndIndex);
		XYZ directionAway = farEnd - intersection;
		double available = directionAway.GetLength();
		double minimumCurve = Math.Max(
			document.Application.ShortCurveTolerance * 2.0,
			UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters));
		if (available <= minimumCurve * 4.0)
		{
			throw new InvalidOperationException(
				$"Không đủ đoạn thẳng trên ống DN {FormatDn(sourcePipe)} để đặt côn tự động.");
		}
		directionAway = directionAway.Normalize();

		double maxDiameter = Math.Max(GetDiameterInternal(sourcePipe), otherDiameter);
		double desiredStub = Math.Max(
			UnitUtils.ConvertToInternalUnits(80.0, UnitTypeId.Millimeters),
			maxDiameter * 1.25);
		double desiredGap = Math.Max(
			UnitUtils.ConvertToInternalUnits(60.0, UnitTypeId.Millimeters),
			maxDiameter * 0.90);
		double maximumTotal = available - minimumCurve * 2.0;
		double scale = Math.Min(1.0, maximumTotal / (desiredStub + desiredGap));
		if (scale < 0.25)
		{
			throw new InvalidOperationException(
				$"Đoạn ống DN {FormatDn(sourcePipe)} quá ngắn để đặt cả côn và fitting.");
		}
		double stubLength = Math.Max(minimumCurve, desiredStub * scale);
		double totalLength = Math.Min(maximumTotal, (desiredStub + desiredGap) * scale);
		if (totalLength - stubLength < minimumCurve)
		{
			stubLength = totalLength - minimumCurve;
		}
		if (stubLength < minimumCurve)
		{
			throw new InvalidOperationException("Không đủ chiều dài hợp lệ cho đoạn ống trung gian của côn.");
		}

		return new TransitionLayout
		{
			OpenEndIndex = openEndIndex,
			SourceTransitionPoint = intersection + directionAway.Multiply(totalLength),
			StubTransitionPoint = intersection + directionAway.Multiply(stubLength)
		};
	}

	private static Line ReplaceOpenEnd(Line sourceLine, int openEndIndex, XYZ newOpenEnd)
	{
		XYZ farEnd = sourceLine.GetEndPoint(1 - openEndIndex);
		return openEndIndex == 0
			? Line.CreateBound(newOpenEnd, farEnd)
			: Line.CreateBound(farEnd, newOpenEnd);
	}

	private static bool HaveSameDiameter(Pipe pipe1, Pipe pipe2)
	{
		return Math.Abs(GetDiameterInternal(pipe1) - GetDiameterInternal(pipe2)) <
			UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Millimeters);
	}

	private static double GetDiameterInternal(Pipe pipe)
	{
		Parameter parameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		if (parameter == null)
		{
			throw new InvalidOperationException($"Không đọc được đường kính của Pipe {pipe.Id.IntegerValue}.");
		}
		double diameter = parameter.AsDouble();
		if (diameter <= 1E-09)
		{
			throw new InvalidOperationException($"Đường kính Pipe {pipe.Id.IntegerValue} không hợp lệ.");
		}
		return diameter;
	}

	private static string FormatDn(Pipe pipe)
	{
		return Math.Round(
			UnitUtils.ConvertFromInternalUnits(GetDiameterInternal(pipe), UnitTypeId.Millimeters),
			1).ToString("0.#", CultureInfo.InvariantCulture);
	}

	private static Line CreateAdjustedLineAtOpenEnd(Pipe pipe, Line line, XYZ intersection)
	{
		Connector openEnd = ConnectorUtilities.FindClosestConnector(pipe.ConnectorManager.Connectors, intersection, endOnly: true, unconnectedOnly: true);
		if (openEnd == null)
		{
			return CreateAdjustedLine(line, intersection);
		}
		XYZ end0 = line.GetEndPoint(0);
		XYZ end1 = line.GetEndPoint(1);
		return (end0.DistanceTo(openEnd.Origin) <= end1.DistanceTo(openEnd.Origin)) ? Line.CreateBound(intersection, end1) : Line.CreateBound(end0, intersection);
	}

	private static XYZ ResolveIntersectionForElbow(Pipe pipe1, Pipe pipe2, Line line1, Line line2, double tolerance)
	{
		Line unbound1 = Line.CreateUnbound(line1.Origin, line1.Direction.Normalize());
		Line unbound2 = Line.CreateUnbound(line2.Origin, line2.Direction.Normalize());
		XYZ directIntersection = TryFindIntersection(unbound1, unbound2);
		if (directIntersection != null)
		{
			return directIntersection;
		}
		XYZ align = GeometryUtilities.Align3D(pipe1, pipe2);
		if (align.GetLength() > 1E-09)
		{
			Line alignedUnbound2 = Line.CreateUnbound(unbound2.Origin + align, unbound2.Direction);
			XYZ alignedIntersection = TryFindIntersection(unbound1, alignedUnbound2);
			if (alignedIntersection != null)
			{
				return alignedIntersection - align.Multiply(0.5);
			}
		}
		if (TryGetClosestPointsMidpoint(unbound1, unbound2, tolerance, out XYZ midpoint))
		{
			return midpoint;
		}
		throw new InvalidOperationException("Cannot determine intersection point for elbow creation.");
	}

	private static Line NormalizeLineByZ(Line line)
	{
		XYZ start = line.GetEndPoint(0);
		XYZ end = line.GetEndPoint(1);
		if (start.Z < end.Z)
		{
			return Line.CreateBound(end, start);
		}
		return line;
	}

	private static Line ExtendLine(Line line, double lengthMm)
	{
		double extension = UnitUtils.ConvertToInternalUnits(lengthMm, UnitTypeId.Millimeters);
		XYZ direction = line.Direction.Normalize().Multiply(extension);
		XYZ newStart = line.GetEndPoint(0) - direction;
		XYZ newEnd = line.GetEndPoint(1) + direction;
		return Line.CreateBound(newStart, newEnd);
	}

	private static Curve PullOntoPlane(Line line, Plane plane)
	{
		XYZ p0 = ProjectPointToPlane(line.GetEndPoint(0), plane);
		XYZ p1 = ProjectPointToPlane(line.GetEndPoint(1), plane);
		return Line.CreateBound(p0, p1);
	}

	private static XYZ ProjectVectorToGlobalXY(XYZ vector)
	{
		return new XYZ(vector.X, vector.Y, 0.0);
	}

	private static XYZ ProjectPointToPlane(XYZ point, Plane plane)
	{
		XYZ originToPoint = point.Subtract(plane.Origin);
		double distance = plane.Normal.DotProduct(originToPoint);
		return point.Subtract(plane.Normal.Multiply(distance));
	}

	private static Parameter? FindWritableAngleParameter(FamilyInstance fitting)
	{
		foreach (Parameter p in fitting.Parameters)
		{
			try
			{
				string typeId = p.GetUnitTypeId().TypeId;
				if (typeId == "autodesk.unit.unit:degrees-1.0.1" && !p.IsReadOnly)
				{
					return p;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static (Connector main1, Connector main2, Connector branch) GetClosestConnectors(Pipe mainPipe, Pipe newMainPipe, Pipe branchPipe)
	{
		ConnectorSet cs1 = mainPipe.ConnectorManager.Connectors;
		ConnectorSet cs2 = newMainPipe.ConnectorManager.Connectors;
		ConnectorSet cs3 = branchPipe.ConnectorManager.Connectors;
		double distance = double.MaxValue;
		Connector c1Best = null;
		Connector c2Best = null;
		Connector c3Best = null;
		foreach (Connector c1 in cs1)
		{
			foreach (Connector c2 in cs2)
			{
				foreach (Connector c3 in cs3)
				{
					double d = (c1.Origin.DistanceTo(c2.Origin) + c1.Origin.DistanceTo(c3.Origin)) / 2.0;
					if (!(d >= distance))
					{
						distance = d;
						c1Best = c1;
						c2Best = c2;
						c3Best = c3;
					}
				}
			}
		}
		if (c1Best == null || c2Best == null || c3Best == null)
		{
			throw new InvalidOperationException("Cannot find connector set for tee.");
		}
		return (main1: c1Best, main2: c2Best, branch: c3Best);
	}

	private static (Pipe pipe1, XYZ? pipe1ConnectionPoint, Pipe pipe2, XYZ? pipe2ConnectionPoint) GetConnectedPipes(FamilyInstance elbow)
	{
		ConnectorManager connectorManager = elbow.MEPModel.ConnectorManager;
		ConnectorSet connectors = connectorManager.Connectors;
		Pipe pipe1 = null;
		Pipe pipe2 = null;
		XYZ p1Point = null;
		XYZ p2Point = null;
		foreach (Connector elbowConnector in connectors)
		{
			if (!elbowConnector.IsConnected)
			{
				continue;
			}
			foreach (Connector refConnector in elbowConnector.AllRefs)
			{
				if (refConnector.Owner.Id == elbow.Id || !(refConnector.Owner is Pipe pipe3))
				{
					continue;
				}
				if (pipe1 == null)
				{
					pipe1 = pipe3;
					p1Point = refConnector.Origin;
				}
				else if (pipe2 == null && pipe3.Id != pipe1.Id)
				{
					pipe2 = pipe3;
					p2Point = refConnector.Origin;
				}
				break;
			}
			if (pipe1 == null || pipe2 == null)
			{
				continue;
			}
			break;
		}
		if (pipe1 == null || pipe2 == null)
		{
			throw new InvalidOperationException("Elbow must connect exactly two pipes.");
		}
		return (pipe1: pipe1, pipe1ConnectionPoint: p1Point, pipe2: pipe2, pipe2ConnectionPoint: p2Point);
	}

	public static List<Pipe> GetConnectedPipesFromFitting(FamilyInstance fitting)
	{
		ConnectorSet connectors = fitting.MEPModel.ConnectorManager.Connectors;
		List<Pipe> pipes = new List<Pipe>();
		HashSet<ElementId> pipeIds = new HashSet<ElementId>();
		foreach (Connector connector in connectors)
		{
			if (!connector.IsConnected)
			{
				continue;
			}
			foreach (Connector refConnector in connector.AllRefs)
			{
				if (!(refConnector.Owner.Id == fitting.Id) && refConnector.Owner is Pipe pipe && pipeIds.Add(pipe.Id))
				{
					pipes.Add(pipe);
				}
			}
		}
		return pipes;
	}

	private static XYZ GetDirectionFromIntersection(XYZ a, Line pipeLine, XYZ? connectionPoint)
	{
		XYZ end0 = pipeLine.GetEndPoint(0);
		XYZ end1 = pipeLine.GetEndPoint(1);
		XYZ target = connectionPoint ?? a;
		XYZ pend = ((end0.DistanceTo(target) <= end1.DistanceTo(target)) ? end0 : end1);
		return (pend - a).Normalize();
	}

	public static double GetElbowLengthInternal(Pipe pipe)
	{
		double diameterInternal = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		int dnMm = (int)Math.Round(UnitUtils.ConvertFromInternalUnits(diameterInternal, UnitTypeId.Millimeters));
		string key = dnMm.ToString(CultureInfo.InvariantCulture);
		string raw = GetData.FromJson("ElbowData", key);
		if (string.IsNullOrWhiteSpace(raw))
		{
			throw new InvalidOperationException($"Missing ElbowData entry for DN(mm)={dnMm}.");
		}
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lMm))
		{
			throw new InvalidOperationException($"Invalid ElbowData value for DN(mm)={dnMm}: '{raw}'.");
		}
		return UnitUtils.ConvertToInternalUnits(lMm, UnitTypeId.Millimeters);
	}

	public static double GetYLengthInternal(Pipe pipeMain, Pipe pipeSub, string target)
	{
		double mainDiameterInternal = ((Element)pipeMain).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		double subDiameterInternal = ((Element)pipeSub).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		int dnMainMm = (int)Math.Round(UnitUtils.ConvertFromInternalUnits(mainDiameterInternal, UnitTypeId.Millimeters));
		int dnSubMm = (int)Math.Round(UnitUtils.ConvertFromInternalUnits(subDiameterInternal, UnitTypeId.Millimeters));
		string key = dnMainMm.ToString(CultureInfo.InvariantCulture) + "-" + dnSubMm.ToString(CultureInfo.InvariantCulture) + "-" + target;
		string raw = GetData.FromJson("YData", key);
		if (string.IsNullOrWhiteSpace(raw))
		{
			// Khi khác DN và project không có reducing Y đúng cặp size, lõi kết
			// nối sẽ tạo Y cùng DN ống chính rồi chèn transition trên nhánh.
			// Vì vậy dùng kích thước hình học của Y đồng cỡ phía ống chính.
			string sameSizeKey = dnMainMm.ToString(CultureInfo.InvariantCulture) + "-" +
				dnMainMm.ToString(CultureInfo.InvariantCulture) + "-" + target;
			raw = GetData.FromJson("YData", sameSizeKey);
			if (string.IsNullOrWhiteSpace(raw))
			{
				throw new InvalidOperationException(
					"Missing YData entry for key='" + key +
					"' and fallback key='" + sameSizeKey + "'.");
			}
		}
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var yMm))
		{
			throw new InvalidOperationException("Invalid YData value for key='" + key + "': '" + raw + "'.");
		}
		return UnitUtils.ConvertToInternalUnits(yMm, UnitTypeId.Millimeters);
	}

	private static XYZ GetUnboundIntersection(Line line1, Line line2)
	{
		Line unbound1 = Line.CreateUnbound(line1.Origin, line1.Direction.Normalize());
		Line unbound2 = Line.CreateUnbound(line2.Origin, line2.Direction.Normalize());
		IntersectionResultArray results;
		SetComparisonResult result = unbound1.Intersect(unbound2, out results);
		if (result == SetComparisonResult.Overlap && results != null && results.Size > 0)
		{
			return results.get_Item(0).XYZPoint;
		}
		throw new InvalidOperationException("Cannot find unbound intersection between two pipe axes.");
	}

	private static XYZ? TryFindIntersection(Line line1, Line line2)
	{
		IntersectionResultArray results;
		SetComparisonResult result = line1.Intersect(line2, out results);
		if (result == SetComparisonResult.Overlap && results != null && results.Size > 0)
		{
			return results.get_Item(0).XYZPoint;
		}
		return null;
	}
}
