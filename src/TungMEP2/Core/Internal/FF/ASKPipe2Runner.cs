using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.FF;

internal static class ASKPipe2Runner
{
	private sealed class AxisData
	{
		internal XYZ Origin { get; }

		internal XYZ Direction { get; }

		internal AxisData(XYZ origin, XYZ direction)
		{
			Origin = origin;
			Direction = direction;
		}
	}

	private sealed class SprinklerNode
	{
		internal FamilyInstance Sprinkler { get; }

		internal XYZ ConnectorOrigin { get; }

		internal XYZ PointOnAxis { get; set; }

		internal double DistanceFromPickOxy { get; set; }

		internal double AxisParameter { get; set; }

		internal SprinklerNode(FamilyInstance sprinkler, XYZ connectorOrigin)
		{
			Sprinkler = sprinkler;
			ConnectorOrigin = connectorOrigin;
			PointOnAxis = connectorOrigin;
		}
	}

	private sealed class PipeCandidate
	{
		internal Pipe Pipe { get; }

		internal double Distance { get; }

		internal double OrientationScore { get; }

		internal double Diameter { get; }

		internal PipeCandidate(Pipe pipe, double distance, double orientationScore, double diameter)
		{
			Pipe = pipe;
			Distance = distance;
			OrientationScore = orientationScore;
			Diameter = diameter;
		}
	}

	private const double Epsilon = 1E-09;

	private const double DN25 = 25.0;

	private const double TeeExtensionMm = 300.0;

	private const double AxisToleranceMm = 5.0;

	internal static bool TryRun(Document document, IList<FamilyInstance> sprinklers, Pipe pipeSample, XYZ pickPointOnPipe, FarthestConnectionMode farthestConnectionMode, FamilyInstance? standardSprinkler, out bool requireStandardSelection, out string error)
	{
		requireStandardSelection = false;
		error = string.Empty;
		if (sprinklers.Count == 0)
		{
			error = "Khong co sprinkler de ket noi.";
			return false;
		}
		if (!TryGetPipeTemplateIds(pipeSample, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId))
		{
			error = "Khong doc duoc template PipeType/System/Level tu pipe sample.";
			return false;
		}
		if (!TryGetPipeLine(pipeSample, out Line pipeSampleLine))
		{
			error = "Khong doc duoc truc hinh hoc cua pipe sample.";
			return false;
		}
		if (!TryGetPipeZAtPoint(pipeSampleLine, pickPointOnPipe, out var trunkZ))
		{
			error = "Khong doc duoc cao do trunk tu pipe sample.";
			return false;
		}
		List<SprinklerNode> nodes = BuildSprinklerNodes(sprinklers);
		if (nodes.Count == 0)
		{
			error = "Khong lay duoc connector piping tu danh sach sprinkler.";
			return false;
		}
		double axisTolerance = UnitUtils.ConvertToInternalUnits(5.0, UnitTypeId.Millimeters);
		if (!TryResolveAxis(document, nodes, pipeSampleLine, trunkZ, axisTolerance, standardSprinkler, out AxisData axis, out requireStandardSelection, out error))
		{
			return false;
		}
		if (!TryMoveSprinklerOnAxisOxy(document, nodes, axis, axisTolerance, out error))
		{
			return false;
		}
		nodes = BuildSprinklerNodes(sprinklers);
		if (nodes.Count == 0)
		{
			error = "Khong doc lai duoc connector sprinkler sau khi canh truc.";
			return false;
		}
		XYZ pickProjection = ProjectPointToAxisOxy(pickPointOnPipe, axis.Origin, axis.Direction, trunkZ);
		foreach (SprinklerNode node in nodes)
		{
			node.PointOnAxis = ProjectPointToAxisOxy(node.ConnectorOrigin, axis.Origin, axis.Direction, trunkZ);
			node.DistanceFromPickOxy = DistanceOxy(node.PointOnAxis, pickProjection);
			node.AxisParameter = (node.PointOnAxis - pickProjection).DotProduct(axis.Direction);
		}
		List<SprinklerNode> orderedNodes = (from n in nodes
			orderby n.DistanceFromPickOxy, n.Sprinkler.Id.Value
			select n).ToList();
		if (orderedNodes.Count == 0)
		{
			error = "Khong co sprinkler hop le de ket noi.";
			return false;
		}
		SprinklerNode farthestNode = orderedNodes[orderedNodes.Count - 1];
		XYZ runDirection = axis.Direction;
		if (farthestNode.AxisParameter < 0.0)
		{
			runDirection = runDirection.Negate();
			foreach (SprinklerNode node2 in orderedNodes)
			{
				node2.AxisParameter = (node2.PointOnAxis - pickProjection).DotProduct(runDirection);
			}
		}
		double startParam = Math.Min(0.0, orderedNodes.Min((SprinklerNode n) => n.AxisParameter));
		double farthestParam = farthestNode.AxisParameter;
		if (farthestParam <= startParam + 1E-09)
		{
			error = "Khong xac dinh duoc huong trunk ASKPipe2.";
			return false;
		}
		double endParam = ((farthestConnectionMode == FarthestConnectionMode.Tee) ? (farthestParam + UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters)) : farthestParam);
		XYZ trunkStart = pickProjection + runDirection.Multiply(startParam);
		XYZ trunkEnd = pickProjection + runDirection.Multiply(endParam);
		if (DistanceOxy(trunkStart, trunkEnd) <= Math.Max(document.Application.ShortCurveTolerance, 1E-06))
		{
			error = "Trunk ASKPipe2 qua ngan de tao.";
			return false;
		}
		double dn25Internal = UnitUtils.ConvertToInternalUnits(25.0, UnitTypeId.Millimeters);
		Pipe trunkPipe;
		using (Transaction tx = new Transaction(document, "ASKPipe2 - Create trunk"))
		{
			tx.Start();
			trunkPipe = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, trunkStart, trunkEnd);
			if (!TrySetPipeDiameter(trunkPipe, dn25Internal))
			{
				tx.RollBack();
				error = "Khong gan duoc DN25 cho trunk ASKPipe2.";
				return false;
			}
			tx.Commit();
		}
		int successCount = 0;
		StringBuilder failures = new StringBuilder();
		List<Pipe> pipeNetwork = CollectConnectedPipes(trunkPipe);
		for (int i = 0; i < orderedNodes.Count; i++)
		{
			SprinklerNode node3 = orderedNodes[i];
			bool useElbowAtFarthest = i == orderedNodes.Count - 1 && farthestConnectionMode == FarthestConnectionMode.Elbow;
			using Transaction tx2 = new Transaction(document, $"ASKPipe2 - Connect SK {node3.Sprinkler.Id.Value}");
			tx2.Start();
			if (!TryGetSprinklerConnector(node3.Sprinkler, out Connector skConnector))
			{
				tx2.RollBack();
				failures.AppendLine($"- SK {node3.Sprinkler.Id.Value}: khong lay duoc connector piping.");
				continue;
			}
			XYZ branchPoint = ProjectPointToAxisOxy(skConnector.Origin, pickProjection, runDirection, trunkZ);
			if (skConnector.Origin.DistanceTo(branchPoint) <= Math.Max(document.Application.ShortCurveTolerance, 1E-06))
			{
				tx2.RollBack();
				failures.AppendLine($"- SK {node3.Sprinkler.Id.Value}: nhanh ket noi qua ngan (khoang cach 3D tu connector den trunk khong du).");
				continue;
			}
			Pipe branchPipe = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, skConnector.Origin, branchPoint);
			Connector branchAtSk = ConnectorUtilities.GetClosestConnector(branchPipe, skConnector.Origin);
			if (!skConnector.IsConnectedTo(branchAtSk))
			{
				skConnector.ConnectTo(branchAtSk);
			}
			double connectorDiameter = ResolveConnectorDiameter(skConnector, dn25Internal);
			if (!TrySetPipeDiameter(branchPipe, connectorDiameter) || !TrySetPipeDiameter(branchPipe, dn25Internal))
			{
				tx2.RollBack();
				failures.AppendLine($"- SK {node3.Sprinkler.Id.Value}: khong gan duoc DN cho nhanh pipe.");
				continue;
			}
			Pipe hostPipe = (useElbowAtFarthest ? FindPipeWithConnectorNearPoint(pipeNetwork, branchPoint) : FindBestHostPipe(pipeNetwork, branchPoint, runDirection, dn25Internal));
			if (hostPipe == null)
			{
				tx2.RollBack();
				failures.AppendLine($"- SK {node3.Sprinkler.Id.Value}: khong tim duoc host pipe.");
				continue;
			}
			try
			{
				if (useElbowAtFarthest)
				{
					branchPipe.CreateElbow(hostPipe);
				}
				else
				{
					PipeUtilities.TeeMaster(hostPipe, branchPipe);
				}
				tx2.Commit();
				successCount++;
				Pipe seedPipe = GetFirstValidPipe(pipeNetwork) ?? hostPipe;
				pipeNetwork = CollectConnectedPipes(seedPipe);
			}
			catch (Exception ex)
			{
				tx2.RollBack();
				failures.AppendLine($"- SK {node3.Sprinkler.Id.Value}: {ex.Message}");
			}
		}
		if (successCount == 0)
		{
			error = ((failures.Length > 0) ? $"Khong ket noi duoc SK nao.\n{failures}" : "Khong ket noi duoc SK nao.");
			return false;
		}
		if (failures.Length > 0)
		{
			error = $"Da ket noi {successCount}/{orderedNodes.Count} SK.\n{failures}";
			return false;
		}
		return true;
	}

	private static bool TryResolveAxis(Document document, List<SprinklerNode> nodes, Line pipeSampleLine, double trunkZ, double tolerance, FamilyInstance? standardSprinkler, out AxisData axis, out bool requireStandardSelection, out string error)
	{
		axis = null;
		requireStandardSelection = false;
		error = string.Empty;
		FindFarthestPair(nodes, out SprinklerNode pointA, out SprinklerNode pointB);
		XYZ a = new XYZ(pointA.ConnectorOrigin.X, pointA.ConnectorOrigin.Y, trunkZ);
		XYZ b = new XYZ(pointB.ConnectorOrigin.X, pointB.ConnectorOrigin.Y, trunkZ);
		XYZ autoDirection = (((b - a).GetLength() > 1E-09) ? (b - a).Normalize() : XYZ.Zero);
		bool isParallel = IsParallelToAxisOxy(autoDirection);
		bool allOnLine = isParallel && nodes.All((SprinklerNode n) => DistancePointToUnboundLineOxy(n.ConnectorOrigin, a, autoDirection) <= tolerance);
		if (isParallel && allOnLine)
		{
			axis = new AxisData(a, autoDirection);
			return true;
		}
		if (standardSprinkler == null)
		{
			requireStandardSelection = true;
			error = "Khong tim duoc truc ASKPipe2 theo cap SK xa nhat. Vui long chon 1 SK lam chuan.";
			return false;
		}
		if (!TryGetSprinklerConnector(standardSprinkler, out Connector standardConnector))
		{
			error = "Khong lay duoc connector piping cua SK chuan.";
			return false;
		}
		if (!(standardSprinkler.Location is LocationPoint standardLocation))
		{
			error = "SK chuan khong co LocationPoint hop le.";
			return false;
		}
		XYZ pipeVectorOxy = new XYZ(pipeSampleLine.Direction.X, pipeSampleLine.Direction.Y, 0.0);
		if (pipeVectorOxy.GetLength() <= 1E-09)
		{
			error = "Pipe sample dang gan thang dung, khong tao duoc truc ASKPipe2.";
			return false;
		}
		Plane planeAtPoint3 = Plane.CreateByNormalAndOrigin(pipeVectorOxy.Normalize(), standardLocation.Point);
		if (!TryIntersectUnboundLineWithPlane(pipeSampleLine, planeAtPoint3, out XYZ point2))
		{
			error = "Khong giao duoc plane SK chuan voi pipe sample de tao point2.";
			return false;
		}
		XYZ point3 = new XYZ(standardLocation.Point.X, standardLocation.Point.Y, point2.Z);
		XYZ point4 = new XYZ(standardConnector.Origin.X, standardConnector.Origin.Y, point2.Z);
		XYZ direction = new XYZ(point3.X - point2.X, point3.Y - point2.Y, 0.0);
		if (direction.GetLength() <= 1E-09)
		{
			error = "Point2 trung Point3 tren OXY, khong tao duoc truc ASKPipe2.";
			return false;
		}
		axis = new AxisData(point2, direction.Normalize());
		if (!(DistancePointToUnboundLineOxy(point4, axis.Origin, axis.Direction) <= tolerance))
		{
			error = "Khong the tao point1 hop le tren truc ASKPipe2 tu SK chuan.";
			return false;
		}
		return true;
	}

	private static bool TryMoveSprinklerOnAxisOxy(Document document, List<SprinklerNode> nodes, AxisData axis, double tolerance, out string error)
	{
		error = string.Empty;
		using Transaction tx = new Transaction(document, "ASKPipe2 - Align sprinkler on axis");
		tx.Start();
		foreach (SprinklerNode node in nodes)
		{
			XYZ target = ProjectPointToAxisOxy(node.ConnectorOrigin, axis.Origin, axis.Direction, node.ConnectorOrigin.Z);
			XYZ delta = new XYZ(target.X - node.ConnectorOrigin.X, target.Y - node.ConnectorOrigin.Y, 0.0);
			if (!(delta.GetLength() <= tolerance))
			{
				try
				{
					ElementTransformUtils.MoveElement(document, node.Sprinkler.Id, delta);
				}
				catch (Exception ex)
				{
					tx.RollBack();
					error = $"Khong the move SK {node.Sprinkler.Id.Value} vao truc ASKPipe2: {ex.Message}";
					return false;
				}
			}
		}
		tx.Commit();
		return true;
	}

	private static List<SprinklerNode> BuildSprinklerNodes(IList<FamilyInstance> sprinklers)
	{
		List<SprinklerNode> nodes = new List<SprinklerNode>();
		foreach (FamilyInstance sprinkler in sprinklers)
		{
			if (TryGetSprinklerConnector(sprinkler, out Connector connector))
			{
				nodes.Add(new SprinklerNode(sprinkler, connector.Origin));
			}
		}
		return nodes;
	}

	private static bool TryGetSprinklerConnector(FamilyInstance sprinkler, out Connector connector)
	{
		connector = null;
		ConnectorSet connectorSet = sprinkler.MEPModel?.ConnectorManager?.Connectors;
		if (connectorSet == null)
		{
			return false;
		}
		List<Connector> connectors = (from Connector c in connectorSet
			where c.Domain == Domain.DomainPiping
			select c).ToList();
		if (connectors.Count == 0)
		{
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
		connector = candidates.OrderBy((Connector c) => c.Origin.Z).First();
		return true;
	}

	private static void FindFarthestPair(List<SprinklerNode> nodes, out SprinklerNode pointA, out SprinklerNode pointB)
	{
		pointA = nodes[0];
		pointB = nodes[0];
		double maxDistance = double.MinValue;
		for (int i = 0; i < nodes.Count; i++)
		{
			for (int j = i + 1; j < nodes.Count; j++)
			{
				double distance = DistanceOxy(nodes[i].ConnectorOrigin, nodes[j].ConnectorOrigin);
				if (!(distance <= maxDistance))
				{
					maxDistance = distance;
					pointA = nodes[i];
					pointB = nodes[j];
				}
			}
		}
	}

	private static bool IsParallelToAxisOxy(XYZ direction)
	{
		XYZ directionOxy = new XYZ(direction.X, direction.Y, 0.0);
		if (directionOxy.GetLength() <= 1E-09)
		{
			return false;
		}
		XYZ normalized = directionOxy.Normalize();
		return Math.Abs(normalized.DotProduct(XYZ.BasisX)) >= 0.999 || Math.Abs(normalized.DotProduct(XYZ.BasisY)) >= 0.999;
	}

	private static XYZ ProjectPointToAxisOxy(XYZ point, XYZ lineOrigin, XYZ lineDirection, double z)
	{
		XYZ directionOxy = new XYZ(lineDirection.X, lineDirection.Y, 0.0).Normalize();
		XYZ originOxy = new XYZ(lineOrigin.X, lineOrigin.Y, 0.0);
		XYZ pointOxy = new XYZ(point.X, point.Y, 0.0);
		double parameter = (pointOxy - originOxy).DotProduct(directionOxy);
		XYZ projectedOxy = originOxy + directionOxy.Multiply(parameter);
		return new XYZ(projectedOxy.X, projectedOxy.Y, z);
	}

	private static double DistancePointToUnboundLineOxy(XYZ point, XYZ lineOrigin, XYZ lineDirection)
	{
		XYZ projection = ProjectPointToAxisOxy(point, lineOrigin, lineDirection, point.Z);
		return DistanceOxy(point, projection);
	}

	private static double DistanceOxy(XYZ a, XYZ b)
	{
		double dx = a.X - b.X;
		double dy = a.Y - b.Y;
		return Math.Sqrt(dx * dx + dy * dy);
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

	private static bool TryGetPipeZAtPoint(Line pipeLine, XYZ point, out double z)
	{
		z = 0.0;
		XYZ start = pipeLine.GetEndPoint(0);
		XYZ end = pipeLine.GetEndPoint(1);
		XYZ direction = (end - start).Normalize();
		double segmentLength = start.DistanceTo(end);
		if (segmentLength <= 1E-09)
		{
			return false;
		}
		double parameter = (point - start).DotProduct(direction);
		parameter = Math.Max(0.0, Math.Min(segmentLength, parameter));
		XYZ closest = start + direction.Multiply(parameter);
		z = closest.Z;
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

	private static double ResolveConnectorDiameter(Connector connector, double fallbackDiameter)
	{
		if (connector.Radius > 1E-09)
		{
			return connector.Radius * 2.0;
		}
		return fallbackDiameter;
	}

	private static List<Pipe> CollectConnectedPipes(Pipe seedPipe)
	{
		Dictionary<long, Pipe> results = new Dictionary<long, Pipe>();
		HashSet<long> visited = new HashSet<long>();
		Queue<Element> queue = new Queue<Element>();
		queue.Enqueue(seedPipe);
		while (queue.Count > 0)
		{
			Element current = queue.Dequeue();
			if (!current.IsValidObject || !visited.Add(current.Id.Value))
			{
				continue;
			}
			if (current is Pipe pipe)
			{
				results[pipe.Id.Value] = pipe;
			}
			foreach (Connector connector in GetElementConnectors(current))
			{
				ConnectorSet allRefs = connector.AllRefs;
				foreach (Connector item in allRefs)
				{
					Element owner = item?.Owner;
					if (owner != null && owner.Id.Value != current.Id.Value && !visited.Contains(owner.Id.Value))
					{
						queue.Enqueue(owner);
					}
				}
			}
		}
		return results.Values.ToList();
	}

	private static IEnumerable<Connector> GetElementConnectors(Element element)
	{
		if (element is Pipe pipe)
		{
			return pipe.ConnectorManager.Connectors.Cast<Connector>();
		}
		if (element is FamilyInstance familyInstance)
		{
			return familyInstance.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>() ?? Enumerable.Empty<Connector>();
		}
		return Enumerable.Empty<Connector>();
	}

	private static Pipe? FindBestHostPipe(List<Pipe> pipeNetwork, XYZ anchor, XYZ axisDirection, double dn25Internal)
	{
		List<PipeCandidate> allCandidates = BuildPipeCandidates(pipeNetwork, anchor, axisDirection);
		if (allCandidates.Count == 0)
		{
			return null;
		}
		List<PipeCandidate> dn25Candidates = allCandidates.Where((PipeCandidate c) => Math.Abs(c.Diameter - dn25Internal) <= 1E-06).ToList();
		PipeCandidate winner = (from c in (dn25Candidates.Count > 0) ? dn25Candidates : allCandidates
			orderby c.Distance, c.OrientationScore descending
			select c).First();
		return winner.Pipe;
	}

	private static List<PipeCandidate> BuildPipeCandidates(List<Pipe> pipeNetwork, XYZ anchor, XYZ axisDirection)
	{
		List<PipeCandidate> result = new List<PipeCandidate>();
		foreach (Pipe pipe in pipeNetwork)
		{
			if (!pipe.IsValidObject || !(pipe.Location is LocationCurve { Curve: Line line }))
			{
				continue;
			}
			XYZ pipeDirection = new XYZ(line.Direction.X, line.Direction.Y, 0.0);
			if (!(pipeDirection.GetLength() <= 1E-09))
			{
				double orientationScore = Math.Abs(pipeDirection.Normalize().DotProduct(axisDirection));
				if (!(orientationScore < 0.98))
				{
					XYZ closestPoint = ClosestPointOnSegment(line.GetEndPoint(0), line.GetEndPoint(1), anchor);
					double distance = closestPoint.DistanceTo(anchor);
					double diameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
					result.Add(new PipeCandidate(pipe, distance, orientationScore, diameter));
				}
			}
		}
		return result;
	}

	private static Pipe? FindPipeWithConnectorNearPoint(List<Pipe> pipeNetwork, XYZ point)
	{
		foreach (Pipe pipe in pipeNetwork)
		{
			if (!pipe.IsValidObject)
			{
				continue;
			}
			foreach (Connector connector in pipe.ConnectorManager.Connectors.Cast<Connector>())
			{
				if (connector.Origin.DistanceTo(point) <= 0.0001)
				{
					return pipe;
				}
			}
		}
		return null;
	}

	private static Pipe? GetFirstValidPipe(List<Pipe> pipes)
	{
		return pipes.FirstOrDefault((Pipe p) => p.IsValidObject);
	}

	private static XYZ ClosestPointOnSegment(XYZ start, XYZ end, XYZ point)
	{
		XYZ segment = end - start;
		double lengthSquared = segment.DotProduct(segment);
		if (lengthSquared <= 1E-09)
		{
			return start;
		}
		double t = (point - start).DotProduct(segment) / lengthSquared;
		t = Math.Max(0.0, Math.Min(1.0, t));
		return start + segment.Multiply(t);
	}
}
