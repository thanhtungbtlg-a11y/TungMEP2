using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal sealed class PipeSpacingBoundaryState
{
	internal PipeSpacingBoundaryState(
		ElementId seedPipeId,
		ElementId pipeId,
		string pipeUniqueId,
		ElementId boundaryOwnerId,
		string boundaryOwnerUniqueId,
		ElementId movingOwnerId,
		string movingOwnerUniqueId,
		XYZ fixedEndpoint,
		XYZ movingEndpoint,
		int fixedEndIndex,
		double originalLength)
	{
		SeedPipeId = seedPipeId;
		PipeId = pipeId;
		PipeUniqueId = pipeUniqueId ?? string.Empty;
		BoundaryOwnerId = boundaryOwnerId;
		BoundaryOwnerUniqueId = boundaryOwnerUniqueId ?? string.Empty;
		MovingOwnerId = movingOwnerId;
		MovingOwnerUniqueId = movingOwnerUniqueId ?? string.Empty;
		FixedEndpoint = fixedEndpoint;
		MovingEndpoint = movingEndpoint;
		FixedEndIndex = fixedEndIndex;
		OriginalLength = originalLength;
	}

	internal ElementId SeedPipeId { get; }
	internal ElementId PipeId { get; }
	internal string PipeUniqueId { get; }
	internal ElementId BoundaryOwnerId { get; }
	internal string BoundaryOwnerUniqueId { get; }
	internal ElementId MovingOwnerId { get; }
	internal string MovingOwnerUniqueId { get; }
	internal XYZ FixedEndpoint { get; }
	internal XYZ MovingEndpoint { get; }
	internal int FixedEndIndex { get; }
	internal double OriginalLength { get; }
}

internal static class PipeSpacingBoundaryService
{
	private const double ConnectorTolerance = 1E-05;

	internal static bool TryCapture(
		Document document,
		PipeSpacingPlan plan,
		out IReadOnlyList<PipeSpacingBoundaryState> states,
		out string error)
	{
		List<PipeSpacingBoundaryState> captured = new List<PipeSpacingBoundaryState>();
		states = captured;
		error = string.Empty;
		if (document == null || plan == null)
		{
			error = "Không có document hoặc phương án PipeSpacing để đọc đoạn biên.";
			return false;
		}

		foreach (PipeSpacingPlanEntry entry in plan.Entries.Where(item => !item.IsAnchor))
		{
			HashSet<long> rigidIds = entry.Cluster.RigidElementIds.Select(id => id.Value).ToHashSet();
			foreach (PipeSpacingBoundaryStretchSegment segment in entry.Cluster.BoundaryStretchSegments)
			{
				if (rigidIds.Contains(segment.PipeId.Value))
				{
					error = $"Pipe biên {segment.PipeId.Value} bị phân loại đồng thời vào lõi dịch cứng.";
					return false;
				}

				if (!(document.GetElement(segment.PipeId) is Pipe pipe) ||
				    !(pipe.Location is LocationCurve { Curve: Line line }))
				{
					error = $"Không đọc được Pipe biên thẳng {segment.PipeId.Value}.";
					return false;
				}

				HashSet<long> boundaryIds = segment.BoundaryOwnerIds.Select(id => id.Value).ToHashSet();
				if (!TryFindConnectedEnd(pipe, boundaryIds, out Connector fixedConnector, out Element boundaryOwner))
				{
					error = $"Pipe biên {pipe.Id.Value} không còn nối vào phần tử ranh giới.";
					return false;
				}
				if (!TryFindConnectedEnd(pipe, rigidIds, out Connector movingConnector, out Element movingOwner))
				{
					error = $"Pipe biên {pipe.Id.Value} không còn nối vào lõi cần dịch chuyển.";
					return false;
				}

				XYZ point0 = line.GetEndPoint(0);
				XYZ point1 = line.GetEndPoint(1);
				int fixedEndIndex = fixedConnector.Origin.DistanceTo(point0) <= fixedConnector.Origin.DistanceTo(point1)
					? 0
					: 1;
				XYZ fixedEndpoint = fixedEndIndex == 0 ? point0 : point1;
				XYZ movingEndpoint = fixedEndIndex == 0 ? point1 : point0;
				if (fixedConnector.Origin.DistanceTo(fixedEndpoint) > ConnectorTolerance ||
				    movingConnector.Origin.DistanceTo(movingEndpoint) > ConnectorTolerance)
				{
					error = $"Connector của Pipe biên {pipe.Id.Value} không trùng hai đầu LocationCurve.";
					return false;
				}

				captured.Add(new PipeSpacingBoundaryState(
					entry.Cluster.SeedPipeId,
					pipe.Id,
					pipe.UniqueId,
					boundaryOwner.Id,
					boundaryOwner.UniqueId,
					movingOwner.Id,
					movingOwner.UniqueId,
					fixedEndpoint,
					movingEndpoint,
					fixedEndIndex,
					line.Length));
			}
		}

		return true;
	}

	internal static bool TryStretchAndVerify(
		Document document,
		PipeSpacingBoundaryState state,
		XYZ rigidMove,
		out string error)
	{
		error = string.Empty;
		if (document == null || state == null || rigidMove == null)
		{
			error = "Thiếu dữ liệu để kéo Pipe biên.";
			return false;
		}

		if (!(document.GetElement(state.PipeId) is Pipe pipe) || pipe.UniqueId != state.PipeUniqueId)
		{
			error = $"Pipe biên {state.PipeId.Value} đã thay đổi sau lúc xem trước.";
			return false;
		}
		Element boundaryOwner = document.GetElement(state.BoundaryOwnerId);
		Element movingOwner = document.GetElement(state.MovingOwnerId);
		if (boundaryOwner == null || boundaryOwner.UniqueId != state.BoundaryOwnerUniqueId ||
		    movingOwner == null || movingOwner.UniqueId != state.MovingOwnerUniqueId)
		{
			error = $"Đầu nối của Pipe biên {state.PipeId.Value} đã thay đổi sau lúc xem trước.";
			return false;
		}

		XYZ newMovingEndpoint = state.MovingEndpoint.Add(rigidMove);
		XYZ originalVector = state.MovingEndpoint.Subtract(state.FixedEndpoint);
		XYZ newVector = newMovingEndpoint.Subtract(state.FixedEndpoint);
		double minimumLength = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
		if (newVector.GetLength() <= minimumLength || originalVector.DotProduct(newVector) <= 0.0)
		{
			error = $"Pipe biên {state.PipeId.Value} sẽ quá ngắn hoặc lật qua đầu cố định.";
			return false;
		}

		if (!(pipe.Location is LocationCurve locationCurve))
		{
			error = $"Pipe biên {state.PipeId.Value} không còn LocationCurve.";
			return false;
		}
		XYZ newPoint0 = state.FixedEndIndex == 0 ? state.FixedEndpoint : newMovingEndpoint;
		XYZ newPoint1 = state.FixedEndIndex == 0 ? newMovingEndpoint : state.FixedEndpoint;
		try
		{
			locationCurve.Curve = Line.CreateBound(newPoint0, newPoint1);
			document.Regenerate();
		}
		catch (Exception exception)
		{
			error = $"Revit từ chối kéo Pipe biên {state.PipeId.Value}: {exception.Message}";
			return false;
		}

		bool fixedConnected = TryVerifyConnectedEndpoint(
			pipe,
			state.FixedEndpoint,
			state.BoundaryOwnerUniqueId,
			out string fixedError);
		bool movingConnected = TryVerifyConnectedEndpoint(
			pipe,
			newMovingEndpoint,
			state.MovingOwnerUniqueId,
			out string movingError);
		if (!fixedConnected || !movingConnected)
		{
			error = $"Pipe biên {state.PipeId.Value} mất kết nối. {fixedError} {movingError}".Trim();
			return false;
		}

		Line finalLine = (pipe.Location as LocationCurve)?.Curve as Line;
		if (finalLine == null ||
		    Math.Min(finalLine.GetEndPoint(0).DistanceTo(state.FixedEndpoint), finalLine.GetEndPoint(1).DistanceTo(state.FixedEndpoint)) > ConnectorTolerance ||
		    Math.Min(finalLine.GetEndPoint(0).DistanceTo(newMovingEndpoint), finalLine.GetEndPoint(1).DistanceTo(newMovingEndpoint)) > ConnectorTolerance)
		{
			error = $"Pipe biên {state.PipeId.Value} không đạt đúng hai đầu sau khi kéo.";
			return false;
		}

		return true;
	}

	private static bool TryFindConnectedEnd(
		Pipe pipe,
		ISet<long> allowedOwnerIds,
		out Connector pipeConnector,
		out Element connectedOwner)
	{
		pipeConnector = null;
		connectedOwner = null;
		foreach (Connector connector in GetPhysicalConnectors(pipe))
		{
			foreach (Connector reference in connector.AllRefs?.Cast<Connector>() ?? Enumerable.Empty<Connector>())
			{
				if (reference?.Owner == null || !allowedOwnerIds.Contains(reference.Owner.Id.Value))
				{
					continue;
				}
				try
				{
					if (!connector.IsConnectedTo(reference))
					{
						continue;
					}
				}
				catch
				{
					continue;
				}
				pipeConnector = connector;
				connectedOwner = reference.Owner;
				return true;
			}
		}
		return false;
	}

	private static bool TryVerifyConnectedEndpoint(
		Pipe pipe,
		XYZ expectedPoint,
		string expectedOwnerUniqueId,
		out string error)
	{
		error = string.Empty;
		Connector connector = GetPhysicalConnectors(pipe)
			.OrderBy(item => item.Origin.DistanceTo(expectedPoint))
			.FirstOrDefault();
		if (connector == null || connector.Origin.DistanceTo(expectedPoint) > ConnectorTolerance)
		{
			error = "Không tìm thấy connector tại đầu Pipe.";
			return false;
		}

		foreach (Connector reference in connector.AllRefs?.Cast<Connector>() ?? Enumerable.Empty<Connector>())
		{
			if (reference?.Owner?.UniqueId != expectedOwnerUniqueId)
			{
				continue;
			}
			try
			{
				if (connector.IsConnectedTo(reference))
				{
					return true;
				}
			}
			catch
			{
				// A missing physical connection is reported below.
			}
		}

		error = "Connector không còn nối với đúng owner ban đầu.";
		return false;
	}

	private static List<Connector> GetPhysicalConnectors(Element element)
	{
		IEnumerable<Connector> connectors = element is MEPCurve mepCurve
			? mepCurve.ConnectorManager?.Connectors?.Cast<Connector>() ?? Enumerable.Empty<Connector>()
			: Enumerable.Empty<Connector>();
		return connectors.Where(connector =>
			connector != null &&
			connector.Domain == Domain.DomainPiping &&
			connector.ConnectorType != ConnectorType.Logical &&
			connector.ConnectorType != ConnectorType.Surface).ToList();
	}
}
