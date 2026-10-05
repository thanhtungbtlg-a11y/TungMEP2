using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.FF;

internal static class SKConnectPipeRunner
{
	private const double Epsilon = 1E-09;

	private const double DN25 = 25.0;

	internal static bool TryConnect(Document document, FamilyInstance sprinkler, Pipe mainPipe, out string error, XYZ? connectorPickPoint = null)
	{
		error = string.Empty;
		if (!TryGetSprinklerConnector(sprinkler, connectorPickPoint, out Connector skConnector, out error))
		{
			return false;
		}
		if (!TryGetPipeLine(mainPipe, out Line mainPipeLine))
		{
			error = "Khong doc duoc truc hinh hoc cua pipe chinh.";
			return false;
		}
		if (!TryCreatePlaneA(skConnector.Origin, mainPipeLine, out Plane planeA, out error))
		{
			return false;
		}
		XYZ point3;
		bool intersectsOnOriginalSegment = TryIntersectBoundedLineWithPlane(mainPipeLine, planeA, out point3);
		if (!intersectsOnOriginalSegment && !TryIntersectUnboundLineWithPlane(mainPipeLine, planeA, out point3))
		{
			error = "Khong tim duoc diem giao (point3) giua Plane A va Pipe.";
			return false;
		}
		XYZ skPoint = skConnector.Origin;
		XYZ point4 = new XYZ(skPoint.X, skPoint.Y, point3.Z);
		double shortTolerance = Math.Max(document.Application.ShortCurveTolerance, 1E-06);
		if (skPoint.DistanceTo(point4) <= shortTolerance || point4.DistanceTo(point3) <= shortTolerance)
		{
			error = "Doan pipe tao moi qua ngan, vui long thu lai voi doi tuong khac.";
			return false;
		}
		if (!TryGetPipeTemplateIds(mainPipe, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId))
		{
			error = "Khong doc duoc template PipeType/System/Level tu pipe chinh.";
			return false;
		}
		double dn25Internal = UnitUtils.ConvertToInternalUnits(25.0, UnitTypeId.Millimeters);
		double sprinklerConnectorDiameter = ResolveConnectorDiameter(skConnector, mainPipe);
		try
		{
			using TransactionGroup transactionGroup = new TransactionGroup(document, "SKConnectPipe");
			transactionGroup.Start();
			Pipe pipe1;
			Pipe pipe2;
			using (Transaction txCreate = new Transaction(document, "SKConnectPipe - Create pipes"))
			{
				txCreate.Start();
				pipe1 = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, skConnector.Origin, point4);
				Connector branchAtSk = ConnectorUtilities.GetClosestConnector(pipe1, skConnector.Origin);
				if (!skConnector.IsConnectedTo(branchAtSk))
				{
					skConnector.ConnectTo(branchAtSk);
				}
				if (!TrySetPipeDiameter(pipe1, sprinklerConnectorDiameter) || !TrySetPipeDiameter(pipe1, dn25Internal))
				{
					txCreate.RollBack();
					error = "Khong the gan kich thuoc cho pipe1.";
					return false;
				}
				pipe2 = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, point4, point3);
				if (!TrySetPipeDiameter(pipe2, dn25Internal))
				{
					txCreate.RollBack();
					error = "Khong the gan kich thuoc DN25 cho pipe2.";
					return false;
				}
				txCreate.Commit();
			}
			using (Transaction txElbowInner = new Transaction(document, "SKConnectPipe - Elbow pipe1 pipe2"))
			{
				txElbowInner.Start();
				pipe1.CreateElbow(pipe2);
				txElbowInner.Commit();
			}
			using (Transaction txConnectMain = new Transaction(document, "SKConnectPipe - Connect main"))
			{
				txConnectMain.Start();
				if (intersectsOnOriginalSegment)
				{
					PipeUtilities.TeeMaster(mainPipe, pipe2);
				}
				else
				{
					pipe2.CreateElbow(mainPipe);
				}
				txConnectMain.Commit();
			}
			transactionGroup.Assimilate();
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
		return true;
	}

	private static bool TryGetSprinklerConnector(FamilyInstance sprinkler, XYZ? pickPoint, out Connector connector, out string error)
	{
		connector = null;
		error = string.Empty;
		ConnectorSet connectorSet = sprinkler.MEPModel?.ConnectorManager?.Connectors;
		if (connectorSet == null)
		{
			error = "Sprinkler khong co connector manager hop le.";
			return false;
		}
		List<Connector> connectors = (from Connector c in connectorSet
			where c.Domain == Domain.DomainPiping
			select c).ToList();
		if (connectors.Count == 0)
		{
			error = "Sprinkler khong co connector piping.";
			return false;
		}
		IEnumerable<Connector> candidates = connectors.Where((Connector c) => c.ConnectorType == ConnectorType.End);
		if (!candidates.Any())
		{
			candidates = connectors;
		}
		IEnumerable<Connector> unconnected = candidates.Where((Connector c) => !c.IsConnected);
		if (unconnected.Any())
		{
			candidates = unconnected;
		}
		XYZ anchor = pickPoint ?? candidates.First().Origin;
		connector = candidates.OrderBy((Connector c) => c.Origin.DistanceTo(anchor)).First();
		return true;
	}

	private static bool TryGetPipeLine(Pipe pipe, out Line line)
	{
		line = null;
		if (!(pipe.Location is LocationCurve locationCurve))
		{
			return false;
		}
		if (!(locationCurve.Curve is Line pipeLine))
		{
			return false;
		}
		line = pipeLine;
		return true;
	}

	private static bool TryCreatePlaneA(XYZ sprinklerConnectorOrigin, Line mainPipeLine, out Plane planeA, out string error)
	{
		planeA = null;
		error = string.Empty;
		XYZ pipeVectorOxy = new XYZ(mainPipeLine.Direction.X, mainPipeLine.Direction.Y, 0.0);
		if (pipeVectorOxy.GetLength() < 1E-09)
		{
			error = "Pipe chinh dang gan thang dung, khong tao duoc Plane A theo vector OXY.";
			return false;
		}
		planeA = Plane.CreateByNormalAndOrigin(pipeVectorOxy.Normalize(), sprinklerConnectorOrigin);
		return true;
	}

	private static bool TryIntersectUnboundLineWithPlane(Line sourceLine, Plane plane, out XYZ point)
	{
		point = null;
		XYZ direction = sourceLine.Direction.Normalize();
		Line unboundLine = Line.CreateUnbound(sourceLine.GetEndPoint(0), direction);
		double denominator = plane.Normal.DotProduct(unboundLine.Direction);
		if (Math.Abs(denominator) < 1E-09)
		{
			return false;
		}
		double parameter = plane.Normal.DotProduct(plane.Origin - unboundLine.Origin) / denominator;
		point = unboundLine.Origin + unboundLine.Direction.Multiply(parameter);
		return true;
	}

	private static bool TryIntersectBoundedLineWithPlane(Line sourceLine, Plane plane, out XYZ point)
	{
		point = null;
		if (!TryIntersectUnboundLineWithPlane(sourceLine, plane, out XYZ candidatePoint))
		{
			return false;
		}
		XYZ startPoint = sourceLine.GetEndPoint(0);
		XYZ endPoint = sourceLine.GetEndPoint(1);
		XYZ lineDirection = sourceLine.Direction.Normalize();
		double segmentLength = startPoint.DistanceTo(endPoint);
		if (segmentLength <= 1E-09)
		{
			return false;
		}
		double projection = (candidatePoint - startPoint).DotProduct(lineDirection);
		double tolerance = Math.Max(segmentLength * 1E-09, 1E-06);
		if (projection < 0.0 - tolerance || projection > segmentLength + tolerance)
		{
			return false;
		}
		point = candidatePoint;
		return true;
	}

	private static bool TryGetPipeTemplateIds(Pipe templatePipe, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId)
	{
		systemTypeId = ElementId.InvalidElementId;
		pipeTypeId = ElementId.InvalidElementId;
		levelId = ElementId.InvalidElementId;
		pipeTypeId = templatePipe.GetTypeId();
		if (pipeTypeId == ElementId.InvalidElementId)
		{
			return false;
		}
		systemTypeId = ((Element)templatePipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
		if (systemTypeId == ElementId.InvalidElementId)
		{
			return false;
		}
		levelId = templatePipe.LevelId;
		if (levelId == ElementId.InvalidElementId)
		{
			levelId = templatePipe.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
		}
		return levelId != ElementId.InvalidElementId;
	}

	private static bool TrySetPipeDiameter(Pipe pipe, double diameter)
	{
		Parameter diameterParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		if (diameterParameter == null || diameterParameter.IsReadOnly || diameter <= 1E-09)
		{
			return false;
		}
		return diameterParameter.Set(diameter);
	}

	private static double ResolveConnectorDiameter(Connector skConnector, Pipe fallbackPipe)
	{
		double connectorDiameter = skConnector.Radius * 2.0;
		if (!IsFinite(connectorDiameter) || connectorDiameter <= 1E-09)
		{
			connectorDiameter = ((Element)fallbackPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		}
		return connectorDiameter;
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
