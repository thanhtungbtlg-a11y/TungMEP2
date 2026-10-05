using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceConnectorService
{
	internal const double MaximumConnectorAxisDeviationDegrees = 5.0;

	// Stable Revit 2024 connector identity is OwnerUniqueId + Connector.Id.
	internal static IReadOnlyList<Connector> GetPhysicalPipingConnectors(Element element)
	{
		ConnectorSet connectors = element switch
		{
			MEPCurve curve => curve.ConnectorManager?.Connectors,
			FamilyInstance instance => instance.MEPModel?.ConnectorManager?.Connectors,
			_ => null
		};

		return connectors?.Cast<Connector>()
			.Where(connector => connector.Domain == Domain.DomainPiping &&
				connector.ConnectorType == ConnectorType.End)
			.OrderBy(connector => connector.Id)
			.ToArray() ?? new Connector[0];
	}

	internal static FitSpaceConnectorKey CreateKey(Connector connector)
	{
		Transform coordinateSystem = connector.CoordinateSystem;
		ConnectorProfileType shape = connector.Shape;
		return new FitSpaceConnectorKey(
			connector.Owner.Id,
			connector.Owner.UniqueId,
			connector.Id,
			connector.Origin,
			coordinateSystem?.BasisX,
			coordinateSystem?.BasisY,
			coordinateSystem?.BasisZ,
			connector.MEPSystem?.Id ?? ElementId.InvalidElementId,
			shape,
			shape == ConnectorProfileType.Round ? connector.Radius : 0.0,
			shape == ConnectorProfileType.Rectangular || shape == ConnectorProfileType.Oval ? connector.Width : 0.0,
			shape == ConnectorProfileType.Rectangular || shape == ConnectorProfileType.Oval ? connector.Height : 0.0);
	}

	internal static Connector FindByKey(Document document, FitSpaceConnectorKey key)
	{
		if (document == null || key == null)
		{
			return null;
		}

		Element owner = document.GetElement(key.OwnerId);
		if (owner == null || owner.UniqueId != key.OwnerUniqueId)
		{
			return null;
		}

		return GetPhysicalPipingConnectors(owner)
			.FirstOrDefault(connector => connector.Id == key.ConnectorId);
	}

	internal static IReadOnlyList<Connector> GetConnectedPhysicalReferences(Connector connector)
	{
		if (connector == null || !connector.IsConnected)
		{
			return new Connector[0];
		}

		return connector.AllRefs.Cast<Connector>()
			.Where(reference => reference.Owner.Id != connector.Owner.Id &&
				reference.Domain == Domain.DomainPiping &&
				reference.ConnectorType == ConnectorType.End)
			.ToArray();
	}

	internal static bool IsConnected(Connector first, Connector second)
	{
		return first != null && second != null &&
			GetConnectedPhysicalReferences(first).Any(reference =>
				reference.Owner.Id == second.Owner.Id && reference.Id == second.Id);
	}

	internal static bool IsConnected(Document document, FitSpaceConnectorBinding binding)
	{
		Connector first = FindByKey(document, binding?.First);
		Connector second = FindByKey(document, binding?.Second);
		return IsConnected(first, second);
	}

	internal static bool ValidateBindingGeometry(
		Document document,
		FitSpaceConnectorBinding binding,
		double originTolerance,
		out string error)
	{
		error = string.Empty;
		Connector first = FindByKey(document, binding?.First);
		Connector second = FindByKey(document, binding?.Second);
		if (first == null || second == null || !IsConnected(first, second))
		{
			error = "Không tìm được cặp connector vật lý đang nối.";
			return false;
		}
		if (first.Origin.DistanceTo(second.Origin) > originTolerance)
		{
			error = "Hai connector đang nối nhưng không trùng vị trí.";
			return false;
		}

		XYZ firstAxis = first.CoordinateSystem?.BasisZ;
		XYZ secondAxis = second.CoordinateSystem?.BasisZ;
		if (firstAxis == null || secondAxis == null)
		{
			error = "Không đọc được trục của cặp connector đang nối.";
			return false;
		}

		double axisDot = firstAxis.Normalize().DotProduct(secondAxis.Normalize());
		if (!FitSpaceMath.IsConnectorAxisOpposed(axisDot, MaximumConnectorAxisDeviationDegrees))
		{
			double deviation = FitSpaceMath.ConnectorAxisDeviationDegrees(axisDot);
			error = $"Hai connector đang nối nhưng trục lệch {deviation:0.##}° so với đối hướng " +
			        $"(cho phép ≤ {MaximumConnectorAxisDeviationDegrees:0.##}°).";
			return false;
		}
		return true;
	}

	internal static bool TryDisconnect(Document document, FitSpaceConnectorBinding binding, out string error)
	{
		error = string.Empty;
		Connector first = FindByKey(document, binding?.First);
		Connector second = FindByKey(document, binding?.Second);
		if (first == null || second == null)
		{
			error = "Không tìm lại được connector cần ngắt.";
			return false;
		}
		if (!IsConnected(first, second))
		{
			error = "Kết nối cục bộ đã thay đổi trước khi thực thi.";
			return false;
		}

		first.DisconnectFrom(second);
		if (IsConnected(first, second))
		{
			error = "Revit không ngắt được kết nối cục bộ.";
			return false;
		}
		return true;
	}

	internal static bool TryReconnect(Document document, FitSpaceConnectorBinding binding, out string error)
	{
		error = string.Empty;
		Connector first = FindByKey(document, binding?.First);
		Connector second = FindByKey(document, binding?.Second);
		if (first == null || second == null)
		{
			error = "Không tìm lại được connector cần nối.";
			return false;
		}
		if (!IsConnected(first, second))
		{
			first.ConnectTo(second);
		}
		if (!IsConnected(first, second))
		{
			error = "Revit không nối lại được connector cục bộ.";
			return false;
		}
		return true;
	}
}
