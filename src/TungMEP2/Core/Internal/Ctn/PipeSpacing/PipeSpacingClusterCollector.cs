using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal sealed class PipeSpacingBoundaryStretchSegment
{
	internal PipeSpacingBoundaryStretchSegment(
		ElementId pipeId,
		IReadOnlyList<ElementId> boundaryOwnerIds)
	{
		PipeId = pipeId ?? throw new ArgumentNullException(nameof(pipeId));
		BoundaryOwnerIds = boundaryOwnerIds ?? Array.Empty<ElementId>();
	}

	internal ElementId PipeId { get; }
	internal IReadOnlyList<ElementId> BoundaryOwnerIds { get; }
}

internal sealed class PipeSpacingCluster
{
	internal PipeSpacingCluster(
		ElementId seedPipeId,
		IReadOnlyList<ElementId> rigidElementIds,
		IReadOnlyList<PipeSpacingBoundaryStretchSegment> boundaryStretchSegments,
		IReadOnlyList<ElementId> boundaryElementIds,
		bool hasPinnedElement,
		bool isConstrained,
		string statusMessage)
	{
		SeedPipeId = seedPipeId ?? throw new ArgumentNullException(nameof(seedPipeId));
		RigidElementIds = rigidElementIds ?? Array.Empty<ElementId>();
		BoundaryStretchSegments = boundaryStretchSegments ?? Array.Empty<PipeSpacingBoundaryStretchSegment>();
		BoundaryElementIds = boundaryElementIds ?? Array.Empty<ElementId>();
		HasPinnedElement = hasPinnedElement;
		IsConstrained = isConstrained;
		StatusMessage = statusMessage ?? string.Empty;
		AllOwnedElementIds = RigidElementIds
			.Concat(BoundaryStretchSegments.Select(segment => segment.PipeId))
			.GroupBy(id => id.Value)
			.Select(group => group.First())
			.ToList();
	}

	internal ElementId SeedPipeId { get; }
	internal IReadOnlyList<ElementId> RigidElementIds { get; }
	internal IReadOnlyList<PipeSpacingBoundaryStretchSegment> BoundaryStretchSegments { get; }
	internal IReadOnlyList<ElementId> BoundaryElementIds { get; }
	internal IReadOnlyList<ElementId> AllOwnedElementIds { get; }
	internal bool HasPinnedElement { get; }
	internal bool IsConstrained { get; }
	internal string StatusMessage { get; }
}

internal static class PipeSpacingClusterCollector
{
	internal static bool TryCollect(
		Document document,
		Pipe seedPipe,
		out PipeSpacingCluster cluster,
		out string error)
	{
		cluster = null;
		error = string.Empty;
		if (document == null || seedPipe == null)
		{
			error = "Không đọc được document hoặc Pipe bắt đầu.";
			return false;
		}

		if (!(seedPipe.Location is LocationCurve { Curve: Line }))
		{
			error = "PipeSpacing chỉ hỗ trợ Pipe thẳng.";
			return false;
		}

		Dictionary<long, Element> elementsById = new Dictionary<long, Element>
		{
			[seedPipe.Id.Value] = seedPipe
		};

		PipeSpacingTraversalNode GetNode(long elementId)
		{
			if (!elementsById.TryGetValue(elementId, out Element element))
			{
				element = document.GetElement(new ElementId(elementId));
				if (element == null)
				{
					throw new InvalidOperationException($"Phần tử {elementId} không còn tồn tại.");
				}
				elementsById[elementId] = element;
			}

			List<long> neighborIds = new List<long>();
			foreach (Connector connector in GetPhysicalPipingConnectors(element))
			{
				ConnectorSet references = connector.AllRefs;
				if (references == null)
				{
					continue;
				}

				foreach (Connector reference in references.Cast<Connector>())
				{
					if (!IsPhysicalConnection(connector, reference))
					{
						continue;
					}

					Element owner = reference.Owner;
					if (owner == null || owner.Id == element.Id)
					{
						continue;
					}

					elementsById[owner.Id.Value] = owner;
					neighborIds.Add(owner.Id.Value);
				}
			}

			return new PipeSpacingTraversalNode(
				element.Id.Value,
				Classify(element),
				neighborIds.Distinct().OrderBy(id => id).ToArray());
		}

		if (!PipeSpacingTraversalPolicy.TryPartition(
				seedPipe.Id.Value,
				GetNode,
				out PipeSpacingTraversalPartition partition,
				out error))
		{
			return false;
		}

		List<ElementId> rigidIds = partition.RigidIds
			.Select(id => new ElementId(id))
			.ToList();
		List<ElementId> boundaryIds = partition.BoundaryIds
			.Select(id => new ElementId(id))
			.ToList();
		HashSet<long> boundaryIdValues = partition.BoundaryIds.ToHashSet();
		List<PipeSpacingBoundaryStretchSegment> stretchSegments = new List<PipeSpacingBoundaryStretchSegment>();
		foreach (long stretchId in partition.BoundaryStretchIds)
		{
			Element stretchElement = document.GetElement(new ElementId(stretchId));
			if (!(stretchElement is Pipe stretchPipe))
			{
				error = $"Phần tử co giãn {stretchId} không còn là Pipe.";
				return false;
			}

			List<ElementId> connectedBoundaryIds = GetPhysicalPipingConnectors(stretchPipe)
				.SelectMany(connector => connector.AllRefs?.Cast<Connector>() ?? Enumerable.Empty<Connector>())
				.Where(reference => reference.Owner != null && boundaryIdValues.Contains(reference.Owner.Id.Value))
				.Where(reference => GetPhysicalPipingConnectors(stretchPipe).Any(connector => IsPhysicalConnection(connector, reference)))
				.Select(reference => reference.Owner.Id)
				.GroupBy(id => id.Value)
				.Select(group => group.First())
				.ToList();
			stretchSegments.Add(new PipeSpacingBoundaryStretchSegment(stretchPipe.Id, connectedBoundaryIds));
		}

		bool constrained = partition.IsConstrained;
		string statusMessage = partition.ConstraintReason;
		bool hasPinnedElement = false;
		IEnumerable<ElementId> modifiedIds = rigidIds.Concat(stretchSegments.Select(segment => segment.PipeId));
		foreach (ElementId modifiedId in modifiedIds)
		{
			Element element = document.GetElement(modifiedId);
			if (element == null)
			{
				constrained = true;
				statusMessage = $"Phần tử {modifiedId.Value} không còn tồn tại.";
				break;
			}
			if (element.Pinned)
			{
				hasPinnedElement = true;
			}
			if (element.GroupId != ElementId.InvalidElementId)
			{
				constrained = true;
				statusMessage = $"Phần tử {modifiedId.Value} trong cụm đang nằm trong Group.";
				break;
			}
		}

		cluster = new PipeSpacingCluster(
			seedPipe.Id,
			rigidIds,
			stretchSegments,
			boundaryIds,
			hasPinnedElement,
			constrained,
			statusMessage);
		return true;
	}

	private static PipeSpacingTraversalNodeKind Classify(Element element)
	{
		if (element is Pipe pipe)
		{
			return pipe.Location is LocationCurve { Curve: Line }
				? PipeSpacingTraversalNodeKind.Pipe
				: PipeSpacingTraversalNodeKind.Unsupported;
		}

		BuiltInCategory? category = GetBuiltInCategory(element);
		if (category == BuiltInCategory.OST_PipeFitting)
		{
			return GetPhysicalPipingConnectors(element).Count == 2
				? PipeSpacingTraversalNodeKind.TwoPortFitting
				: PipeSpacingTraversalNodeKind.Boundary;
		}

		if (category == BuiltInCategory.OST_PipeAccessory ||
		    category == BuiltInCategory.OST_MechanicalEquipment ||
		    category == BuiltInCategory.OST_PlumbingFixtures)
		{
			return PipeSpacingTraversalNodeKind.Boundary;
		}

		return PipeSpacingTraversalNodeKind.Unsupported;
	}

	private static BuiltInCategory? GetBuiltInCategory(Element element)
	{
		if (element?.Category?.Id == null)
		{
			return null;
		}
		return (BuiltInCategory)element.Category.Id.Value;
	}

	private static List<Connector> GetPhysicalPipingConnectors(Element element)
	{
		IEnumerable<Connector> connectors = Enumerable.Empty<Connector>();
		if (element is MEPCurve mepCurve)
		{
			connectors = mepCurve.ConnectorManager?.Connectors?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
		}
		else if (element is FamilyInstance familyInstance)
		{
			connectors = familyInstance.MEPModel?.ConnectorManager?.Connectors?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
		}

		return connectors
			.Where(connector => connector != null &&
			                    connector.Domain == Domain.DomainPiping &&
			                    connector.ConnectorType != ConnectorType.Logical &&
			                    connector.ConnectorType != ConnectorType.Surface)
			.ToList();
	}

	private static bool IsPhysicalConnection(Connector connector, Connector reference)
	{
		if (connector == null || reference?.Owner == null ||
		    reference.Domain != Domain.DomainPiping ||
		    reference.ConnectorType == ConnectorType.Logical ||
		    reference.ConnectorType == ConnectorType.Surface)
		{
			return false;
		}

		try
		{
			return connector.IsConnectedTo(reference);
		}
		catch
		{
			return false;
		}
	}
}
