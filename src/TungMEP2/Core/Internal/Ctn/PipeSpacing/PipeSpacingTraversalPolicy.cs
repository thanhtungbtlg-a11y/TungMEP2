using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal enum PipeSpacingTraversalNodeKind
{
	Pipe,
	TwoPortFitting,
	Boundary,
	Unsupported
}

internal readonly struct PipeSpacingTraversalNode
{
	internal PipeSpacingTraversalNode(
		long id,
		PipeSpacingTraversalNodeKind kind,
		IReadOnlyList<long> neighborIds)
	{
		Id = id;
		Kind = kind;
		NeighborIds = neighborIds ?? Array.Empty<long>();
	}

	internal long Id { get; }
	internal PipeSpacingTraversalNodeKind Kind { get; }
	internal IReadOnlyList<long> NeighborIds { get; }
}

internal sealed class PipeSpacingTraversalPartition
{
	internal PipeSpacingTraversalPartition(
		IReadOnlyList<long> rigidIds,
		IReadOnlyList<long> boundaryStretchIds,
		IReadOnlyList<long> boundaryIds,
		bool isConstrained,
		string constraintReason)
	{
		RigidIds = rigidIds ?? Array.Empty<long>();
		BoundaryStretchIds = boundaryStretchIds ?? Array.Empty<long>();
		BoundaryIds = boundaryIds ?? Array.Empty<long>();
		IsConstrained = isConstrained;
		ConstraintReason = constraintReason ?? string.Empty;
	}

	internal IReadOnlyList<long> RigidIds { get; }
	internal IReadOnlyList<long> BoundaryStretchIds { get; }
	internal IReadOnlyList<long> BoundaryIds { get; }
	internal bool IsConstrained { get; }
	internal string ConstraintReason { get; }
}

internal static class PipeSpacingTraversalPolicy
{
	internal static bool TryPartition(
		long seedId,
		Func<long, PipeSpacingTraversalNode> getNode,
		out PipeSpacingTraversalPartition partition,
		out string error)
	{
		partition = null;
		error = string.Empty;
		if (getNode == null)
		{
			error = "Không có nguồn dữ liệu connector để thu thập cụm.";
			return false;
		}

		PipeSpacingTraversalNode seed;
		try
		{
			seed = getNode(seedId);
		}
		catch (Exception exception)
		{
			error = "Không đọc được Pipe bắt đầu: " + exception.Message;
			return false;
		}

		if (seed.Id != seedId || seed.Kind != PipeSpacingTraversalNodeKind.Pipe)
		{
			error = "Phần tử bắt đầu phải là Pipe hợp lệ.";
			return false;
		}

		Queue<long> pending = new Queue<long>();
		HashSet<long> visited = new HashSet<long>();
		Dictionary<long, PipeSpacingTraversalNode> nodes = new Dictionary<long, PipeSpacingTraversalNode>();
		HashSet<long> boundaryIds = new HashSet<long>();
		HashSet<long> stretchIds = new HashSet<long>();
		Dictionary<long, int> boundaryEdgesByPipe = new Dictionary<long, int>();
		bool unsupportedAtFitting = false;
		pending.Enqueue(seedId);

		while (pending.Count > 0)
		{
			long currentId = pending.Dequeue();
			if (!visited.Add(currentId))
			{
				continue;
			}

			PipeSpacingTraversalNode current;
			try
			{
				current = getNode(currentId);
			}
			catch (Exception exception)
			{
				error = $"Không đọc được phần tử kết nối {currentId}: {exception.Message}";
				return false;
			}

			if (!IsTraversable(current.Kind))
			{
				error = $"Đã duyệt nhầm vào phần tử ranh giới {currentId}.";
				return false;
			}

			nodes[currentId] = current;
			foreach (long neighborId in current.NeighborIds.Distinct().OrderBy(id => id))
			{
				PipeSpacingTraversalNode neighbor;
				try
				{
					neighbor = getNode(neighborId);
				}
				catch (Exception exception)
				{
					error = $"Không đọc được phần tử lân cận {neighborId}: {exception.Message}";
					return false;
				}

				if (IsTraversable(neighbor.Kind))
				{
					if (!visited.Contains(neighborId))
					{
						pending.Enqueue(neighborId);
					}
					continue;
				}

				boundaryIds.Add(neighborId);
				if (current.Kind == PipeSpacingTraversalNodeKind.Pipe)
				{
					stretchIds.Add(currentId);
					boundaryEdgesByPipe[currentId] =
						boundaryEdgesByPipe.TryGetValue(currentId, out int count)
							? count + 1
							: 1;
				}
				else
				{
					unsupportedAtFitting = true;
				}
			}
		}

		List<long> rigidIds = visited
			.Where(id => !stretchIds.Contains(id))
			.OrderBy(id => id)
			.ToList();
		bool hasRigidPipe = rigidIds.Any(id =>
			nodes.TryGetValue(id, out PipeSpacingTraversalNode node) &&
			node.Kind == PipeSpacingTraversalNodeKind.Pipe);
		bool multiplyFixedStretch = boundaryEdgesByPipe.Values.Any(count => count > 1);
		bool isConstrained = stretchIds.Contains(seedId) ||
		                     !hasRigidPipe ||
		                     multiplyFixedStretch ||
		                     unsupportedAtFitting;
		string reason = string.Empty;
		if (stretchIds.Contains(seedId))
		{
			reason = "Pipe được chọn chạm trực tiếp phần tử ranh giới nên không còn lõi dịch cứng.";
		}
		else if (!hasRigidPipe)
		{
			reason = "Cụm không còn đoạn Pipe lõi có thể dịch chuyển.";
		}
		else if (multiplyFixedStretch)
		{
			reason = "Một đoạn Pipe ranh giới đang bị giữ bởi nhiều đầu cố định.";
		}
		else if (unsupportedAtFitting)
		{
			reason = "Fitting nối trực tiếp vào ranh giới mà không có đoạn Pipe co giãn an toàn.";
		}

		partition = new PipeSpacingTraversalPartition(
			rigidIds,
			stretchIds.OrderBy(id => id).ToArray(),
			boundaryIds.OrderBy(id => id).ToArray(),
			isConstrained,
			reason);
		return true;
	}

	private static bool IsTraversable(PipeSpacingTraversalNodeKind kind)
	{
		return kind == PipeSpacingTraversalNodeKind.Pipe ||
		       kind == PipeSpacingTraversalNodeKind.TwoPortFitting;
	}
}

