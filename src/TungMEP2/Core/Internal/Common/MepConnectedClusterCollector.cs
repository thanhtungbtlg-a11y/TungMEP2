using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Common;

internal sealed class MepConnectedCluster
{
	internal IReadOnlyList<Element> Elements { get; }

	internal ICollection<ElementId> ElementIds { get; }

	internal MepConnectedCluster(IReadOnlyList<Element> elements)
	{
		Elements = elements;
		ElementIds = elements.Select(element => element.Id).ToList();
	}
}

internal static class MepConnectedClusterCollector
{
	internal static bool TryCollect(
		Element movingElement,
		Element fixedElement,
		out MepConnectedCluster cluster,
		out string error)
	{
		cluster = null;
		error = string.Empty;
		if (movingElement == null || fixedElement == null)
		{
			error = "Không đọc được phần tử mốc hoặc phần tử cần di chuyển.";
			return false;
		}

		Dictionary<long, Element> elementsById =
			new Dictionary<long, Element>
			{
				[movingElement.Id.Value] = movingElement
			};

		long[] GetConnectedElementIds(long elementId)
		{
			if (!elementsById.TryGetValue(elementId, out Element element))
			{
				return Array.Empty<long>();
			}

			List<long> connectedIds = new List<long>();
			foreach (Connector connector in GetPhysicalConnectors(element))
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
					long ownerId = owner.Id.Value;
					if (ownerId == elementId)
					{
						continue;
					}

					elementsById[ownerId] = owner;
					connectedIds.Add(ownerId);
				}
			}

			return connectedIds.Distinct().ToArray();
		}

		if (!TryTraverseConnectedIds(
				movingElement.Id.Value,
				fixedElement.Id.Value,
				GetConnectedElementIds,
				out long[] connectedElementIds))
		{
			error =
				"Hai phần tử thuộc cùng mạng liên thông; không thể di chuyển cả cụm.";
			return false;
		}

		List<Element> elements = connectedElementIds
			.Select(elementId => elementsById[elementId])
			.ToList();
		foreach (Element element in elements)
		{
			if (!IsTransformableMepElement(element))
			{
				error =
					$"Phần tử {element.Id.Value} ({element.Name}) không thể di chuyển an toàn cùng cụm MEP.";
				return false;
			}

			if (element.Pinned)
			{
				error =
					$"Phần tử {element.Id.Value} ({element.Name}) trong cụm đang bị Pin.";
				return false;
			}

			if (element.GroupId != ElementId.InvalidElementId)
			{
				error =
					$"Phần tử {element.Id.Value} ({element.Name}) trong cụm đang nằm trong Group.";
				return false;
			}
		}

		cluster = new MepConnectedCluster(elements);
		return true;
	}

	internal static bool TryTraverseConnectedIds(
		long movingElementId,
		long fixedElementId,
		Func<long, long[]> getConnectedElementIds,
		out long[] connectedElementIds)
	{
		connectedElementIds = Array.Empty<long>();
		if (getConnectedElementIds == null || movingElementId == fixedElementId)
		{
			return false;
		}

		Queue<long> pending = new Queue<long>();
		HashSet<long> visited = new HashSet<long>();
		List<long> orderedIds = new List<long>();
		pending.Enqueue(movingElementId);
		while (pending.Count > 0)
		{
			long currentId = pending.Dequeue();
			if (!visited.Add(currentId))
			{
				continue;
			}

			if (currentId == fixedElementId)
			{
				return false;
			}

			orderedIds.Add(currentId);
			long[] neighborIds =
				getConnectedElementIds(currentId) ?? Array.Empty<long>();
			foreach (long neighborId in neighborIds)
			{
				if (!visited.Contains(neighborId))
				{
					pending.Enqueue(neighborId);
				}
			}
		}

		connectedElementIds = orderedIds.ToArray();
		return true;
	}

	internal static List<Connector> GetPhysicalConnectors(Element element)
	{
		IEnumerable<Connector> connectors = Enumerable.Empty<Connector>();
		if (element is MEPCurve mepCurve)
		{
			connectors = mepCurve.ConnectorManager?.Connectors?.Cast<Connector>() ??
				Enumerable.Empty<Connector>();
		}
		else if (element is FamilyInstance familyInstance)
		{
			connectors = familyInstance.MEPModel?.ConnectorManager?.Connectors
				?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
		}
		else if (element is FabricationPart fabricationPart)
		{
			connectors = fabricationPart.ConnectorManager?.Connectors
				?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
		}

		return connectors
			.Where(connector =>
				connector != null &&
				connector.ConnectorType != ConnectorType.Logical)
			.ToList();
	}

	private static bool IsPhysicalConnection(
		Connector connector,
		Connector reference)
	{
		if (reference == null ||
			reference.Owner == null ||
			reference.ConnectorType == ConnectorType.Logical)
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

	private static bool IsTransformableMepElement(Element element)
	{
		return element is MEPCurve ||
		       element is FamilyInstance ||
		       element is FabricationPart;
	}
}
