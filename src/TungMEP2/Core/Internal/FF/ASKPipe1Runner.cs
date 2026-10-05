using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Internal.FF;

internal static class ASKPipe1Runner
{
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

	internal static bool TryRun(Document document, IList<FamilyInstance> sprinklers, Pipe pipeSample, XYZ point1, XYZ point2, out string error)
	{
		error = string.Empty;
		if (sprinklers.Count == 0)
		{
			error = "Khong co sprinkler de ket noi.";
			return false;
		}
		if (!TryCreateTrunkLine(point1, point2, pipeSample, document, out Line trunkLine, out error))
		{
			return false;
		}
		if (!TryGetPipeTemplateIds(pipeSample, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId))
		{
			error = "Khong doc duoc template PipeType/System/Level tu pipe sample.";
			return false;
		}
		double dn25Internal = UnitUtils.ConvertToInternalUnits(25.0, UnitTypeId.Millimeters);
		Pipe trunkPipe;
		using (Transaction tx = new Transaction(document, "ASKPipe1 - Create trunk"))
		{
			tx.Start();
			trunkPipe = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, trunkLine.GetEndPoint(0), trunkLine.GetEndPoint(1));
			if (!TrySetPipeDiameter(trunkPipe, dn25Internal))
			{
				tx.RollBack();
				error = "Khong gan duoc DN25 cho trunk pipe.";
				return false;
			}
			tx.Commit();
		}
		XYZ trunkDirection = (trunkLine.GetEndPoint(1) - trunkLine.GetEndPoint(0)).Normalize();
		List<Pipe> pipeNetwork = CollectConnectedPipes(trunkPipe);
		List<FamilyInstance> orderedSprinklers = sprinklers.OrderBy((FamilyInstance sk) => ComputeAlongParameter(GetSprinklerAnchor(sk), trunkLine.GetEndPoint(0), trunkDirection)).ToList();
		int successCount = 0;
		StringBuilder failures = new StringBuilder();
		for (int i = 0; i < orderedSprinklers.Count; i++)
		{
			FamilyInstance sprinkler = orderedSprinklers[i];
			XYZ anchor = GetSprinklerAnchor(sprinkler);
			Pipe hostPipe = FindBestHostPipe(pipeNetwork, anchor, trunkDirection, dn25Internal);
			if (hostPipe == null)
			{
				failures.AppendLine($"- SK {sprinkler.Id.Value}: khong tim duoc host pipe phu hop.");
				continue;
			}
			if (!SKConnectPipeRunner.TryConnect(document, sprinkler, hostPipe, out string connectError, anchor))
			{
				failures.AppendLine($"- SK {sprinkler.Id.Value}: {connectError}");
				continue;
			}
			successCount++;
			Pipe seedPipe = GetFirstValidPipe(pipeNetwork) ?? hostPipe;
			pipeNetwork = CollectConnectedPipes(seedPipe);
		}
		if (successCount == 0)
		{
			error = ((failures.Length > 0) ? $"Khong ket noi duoc SK nao.\n{failures}" : "Khong ket noi duoc SK nao.");
			return false;
		}
		if (failures.Length > 0)
		{
			error = $"Da ket noi {successCount}/{orderedSprinklers.Count} SK.\n{failures}";
			return false;
		}
		return true;
	}

	private static bool TryCreateTrunkLine(XYZ point1, XYZ point2, Pipe pipeSample, Document document, out Line line, out string error)
	{
		line = null;
		error = string.Empty;
		XYZ point1Flat = new XYZ(point1.X, point1.Y, 0.0);
		XYZ point2Flat = new XYZ(point2.X, point2.Y, 0.0);
		if (point1Flat.DistanceTo(point2Flat) <= 1E-09)
		{
			error = "Point2 trung voi Point1 tren mat bang XY.";
			return false;
		}
		Plane PlaneOX = Plane.CreateByNormalAndOrigin(XYZ.BasisY, point1Flat);
		Plane PlaneOY = Plane.CreateByNormalAndOrigin(XYZ.BasisX, point1Flat);
		XYZ p1Ox = ProjectPointToPlane(point1Flat, PlaneOX);
		XYZ p2Ox = ProjectPointToPlane(point2Flat, PlaneOX);
		XYZ p1Oy = ProjectPointToPlane(point1Flat, PlaneOY);
		XYZ p2Oy = ProjectPointToPlane(point2Flat, PlaneOY);
		double dOx = p1Ox.DistanceTo(p2Ox);
		double dOy = p1Oy.DistanceTo(p2Oy);
		XYZ selectedStart = ((dOx >= dOy) ? p1Ox : p1Oy);
		XYZ selectedEnd = ((dOx >= dOy) ? p2Ox : p2Oy);
		if (!TryGetPipeZAtPoint(pipeSample, point1, out var pipeZ))
		{
			error = "Khong doc duoc Z location cua Pipe sample tai point1.";
			return false;
		}
		selectedStart = new XYZ(selectedStart.X, selectedStart.Y, pipeZ);
		selectedEnd = new XYZ(selectedEnd.X, selectedEnd.Y, pipeZ);
		if (selectedStart.DistanceTo(selectedEnd) <= Math.Max(document.Application.ShortCurveTolerance, 1E-06))
		{
			error = "Chieu dai trunk moi qua ngan.";
			return false;
		}
		line = Line.CreateBound(selectedStart, selectedEnd);
		return true;
	}

	private static XYZ ProjectPointToPlane(XYZ point, Plane plane)
	{
		double distance = plane.Normal.DotProduct(point - plane.Origin);
		return point - plane.Normal.Multiply(distance);
	}

	private static bool TryGetPipeZAtPoint(Pipe pipe, XYZ point, out double z)
	{
		z = 0.0;
		if (!(pipe.Location is LocationCurve { Curve: Line line }))
		{
			return false;
		}
		XYZ start = line.GetEndPoint(0);
		XYZ end = line.GetEndPoint(1);
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

	private static Pipe? FindBestHostPipe(List<Pipe> pipeNetwork, XYZ anchor, XYZ trunkDirection, double dn25Internal)
	{
		List<PipeCandidate> allCandidates = BuildPipeCandidates(pipeNetwork, anchor, trunkDirection);
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

	private static List<PipeCandidate> BuildPipeCandidates(List<Pipe> pipeNetwork, XYZ anchor, XYZ trunkDirection)
	{
		List<PipeCandidate> result = new List<PipeCandidate>();
		foreach (Pipe pipe in pipeNetwork)
		{
			if (pipe.IsValidObject && pipe.Location is LocationCurve { Curve: Line line })
			{
				XYZ pipeDirection = line.Direction.Normalize();
				double orientationScore = Math.Abs(pipeDirection.DotProduct(trunkDirection));
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

	private static XYZ GetSprinklerAnchor(FamilyInstance sprinkler)
	{
		ConnectorSet connectors = sprinkler.MEPModel?.ConnectorManager?.Connectors;
		if (connectors == null)
		{
			return (sprinkler.Location as LocationPoint)?.Point ?? XYZ.Zero;
		}
		return (from Connector c in connectors
			where c.Domain == Domain.DomainPiping
			orderby c.IsConnected, c.Origin.Z
			select c).FirstOrDefault()?.Origin ?? (sprinkler.Location as LocationPoint)?.Point ?? XYZ.Zero;
	}

	private static double ComputeAlongParameter(XYZ point, XYZ start, XYZ direction)
	{
		return (point - start).DotProduct(direction);
	}

	private static Pipe? GetFirstValidPipe(List<Pipe> pipes)
	{
		return pipes.FirstOrDefault((Pipe p) => p.IsValidObject);
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
}
