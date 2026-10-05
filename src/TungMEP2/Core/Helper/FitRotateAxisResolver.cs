using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class FitRotateAxisResolver
{
	private const double ParallelTolerance = 0.001;

	private const double ConnectorDistanceTolerance = 0.0001;

	public static bool TryGetAxisLineFromStraightSegment(Element axisElement, out Line? axisLine, out string error)
	{
		axisLine = null;
		error = string.Empty;
		if (axisElement == null)
		{
			error = "Khong tim thay phan tu truc xoay.";
			return false;
		}
		List<Connector> axisConnectors = GetNonSurfaceConnectors(axisElement);
		if (axisConnectors.Count < 2)
		{
			error = "Truc xoay phai la segment MEP thang va co it nhat 2 connector.";
			return false;
		}
		Connector first = axisConnectors[0];
		Connector second = axisConnectors.OrderByDescending((Connector c) => c.Origin.DistanceTo(first.Origin)).First();
		if (first.Origin.DistanceTo(second.Origin) <= 0.0001)
		{
			error = "Khong xac dinh duoc huong truc xoay tu segment da chon.";
			return false;
		}
		axisLine = Line.CreateBound(first.Origin, second.Origin);
		return true;
	}

	public static bool TryGetAxisLineFromConnectedSegment(Element axisElement, IEnumerable<ElementId> rotateElementIds, Document document, out Line? axisLine, out string error)
	{
		axisLine = null;
		error = string.Empty;
		if (axisElement == null)
		{
			error = "Không tìm thấy đoạn ống làm trục xoay.";
			return false;
		}
		List<Connector> axisConnectors = GetNonSurfaceConnectors(axisElement);
		if (axisConnectors.Count < 2)
		{
			error = "Đoạn được chọn không phải segment MEP thẳng hợp lệ để làm trục xoay.";
			return false;
		}
		List<Connector> selectedConnectors = (from e in rotateElementIds.Select(document.GetElement)
			where e != null
			select e).SelectMany(GetNonSurfaceConnectors).ToList();
		Connector connectedConnector = axisConnectors.FirstOrDefault((Connector axisConnector) => selectedConnectors.Any((Connector selectedConnector) => AreConnectedOrCoincident(axisConnector, selectedConnector)));
		Connector pivotConnector = connectedConnector;
		if (pivotConnector == null)
		{
			error =
				"Đoạn làm trục không nối trực tiếp với cụm đã chọn.\n\n" +
				"Hãy chọn đoạn ống/ống gió thẳng đang nối vào fitting hoặc nhánh cần xoay. " +
				"FitRotate không còn dùng đoạn gần nhất vì có thể làm lệch cụm và phá kết nối.";
			return false;
		}
		Connector otherConnector = (from c in axisConnectors
			where c.Origin.DistanceTo(pivotConnector.Origin) > 0.0001
			orderby c.Origin.DistanceTo(pivotConnector.Origin) descending
			select c).FirstOrDefault();
		if (otherConnector == null)
		{
			error = "Khong tim thay dau con lai cua segment de tao truc xoay.";
			return false;
		}
		axisLine = Line.CreateBound(otherConnector.Origin, pivotConnector.Origin);
		return true;
	}

	public static bool TryGetAxisLineFromSingleFittingConnectors(Element fitting, out Line? axisLine, out string error)
	{
		axisLine = null;
		error = string.Empty;
		List<Connector> connectors = GetNonSurfaceConnectors(fitting);
		if (connectors.Count < 2)
		{
			error = "Fitting duoc chon khong du connector de xac dinh truc xoay.";
			return false;
		}
		if (connectors.Count > 3)
		{
			error = "Fitting có quá 3 connector nên không thể tự động suy ra trục xoay.";
			return false;
		}

		List<Connector> externallyConnectedConnectors = connectors
			.Where(connector => IsExternallyConnected(connector, fitting.Id))
			.ToList();
		if (externallyConnectedConnectors.Count > 1)
		{
			error =
				$"Fitting đang nối kín {externallyConnectedConnectors.Count} đầu nên không thể xoay riêng mà vẫn giữ mọi kết nối.\n\n" +
				"Hãy chọn trước fitting cùng toàn bộ nhánh cần di chuyển (ít nhất 2 phần tử), " +
				"sau đó chạy FitRotate và chọn đoạn ống thẳng nối trực tiếp làm trục cố định.";
			return false;
		}
		if (externallyConnectedConnectors.Count == 1)
		{
			Connector pivot = externallyConnectedConnectors[0];
			XYZ axisDirection = pivot.CoordinateSystem.BasisZ;
			if (axisDirection != null && axisDirection.GetLength() > 1E-09)
			{
				axisLine = Line.CreateBound(pivot.Origin, pivot.Origin + axisDirection.Normalize());
				return true;
			}
		}

		if (TryResolveTapAxis(fitting, connectors, out axisLine))
		{
			return true;
		}
		if (connectors.Count == 2)
		{
			axisLine = ResolveTwoConnectorAxis(connectors[0], connectors[1]);
			return axisLine != null;
		}
		axisLine = ResolveThreeConnectorAxis(connectors[0], connectors[1], connectors[2]);
		if (axisLine != null)
		{
			return true;
		}
		error = "Khong the suy ra truc xoay tu he connector cua fitting.";
		return false;
	}

	private static Line? ResolveTwoConnectorAxis(Connector first, Connector second)
	{
		if (AreParallelOrOpposite(first.CoordinateSystem.BasisZ, second.CoordinateSystem.BasisZ))
		{
			if (Average(first) > Average(second))
			{
				return Line.CreateBound(first.Origin, second.Origin);
			}
			return Line.CreateBound(second.Origin, first.Origin);
		}
		Connector pivot = (first.IsConnected ? first : second);
		XYZ axisEnd = pivot.Origin + pivot.CoordinateSystem.BasisZ;
		return Line.CreateBound(pivot.Origin, axisEnd);
	}

	private static Line? ResolveThreeConnectorAxis(Connector c0, Connector c1, Connector c2)
	{
		if (AreParallelOrOpposite(c0.CoordinateSystem.BasisZ, c1.CoordinateSystem.BasisZ))
		{
			return (Average(c0) > Average(c1)) ? Line.CreateBound(c0.Origin, c1.Origin) : Line.CreateBound(c1.Origin, c0.Origin);
		}
		if (AreParallelOrOpposite(c0.CoordinateSystem.BasisZ, c2.CoordinateSystem.BasisZ))
		{
			return (Average(c0) > Average(c2)) ? Line.CreateBound(c0.Origin, c2.Origin) : Line.CreateBound(c2.Origin, c0.Origin);
		}
		return (Average(c1) > Average(c2)) ? Line.CreateBound(c1.Origin, c2.Origin) : Line.CreateBound(c2.Origin, c1.Origin);
	}

	private static bool TryResolveTapAxis(Element fitting, IReadOnlyCollection<Connector> connectors, out Line? axisLine)
	{
		axisLine = null;
		if (connectors.Count != 2 || !IsTapFitting(fitting))
		{
			return false;
		}
		Connector linkedConnector = null;
		Connector matchingConnector = null;
		foreach (Connector connector in connectors)
		{
			foreach (Connector connectorRef in connector.AllRefs.Cast<Connector>())
			{
				if (!(connectorRef.Owner.Id == fitting.Id))
				{
					linkedConnector = connectorRef;
					matchingConnector = connector;
					if (IsMatchingTapAxisConnector(linkedConnector, matchingConnector))
					{
						break;
					}
				}
			}
			if (linkedConnector != null && matchingConnector != null && IsMatchingTapAxisConnector(linkedConnector, matchingConnector))
			{
				break;
			}
		}
		if (linkedConnector == null)
		{
			return false;
		}
		axisLine = Line.CreateBound(linkedConnector.Origin, linkedConnector.Origin + linkedConnector.CoordinateSystem.BasisZ);
		return true;
	}

	private static bool IsMatchingTapAxisConnector(Connector externalConnector, Connector fittingConnector)
	{
		double dot = externalConnector.CoordinateSystem.BasisZ.DotProduct(fittingConnector.CoordinateSystem.BasisZ);
		if (Math.Abs(dot + 1.0) > 0.001)
		{
			return false;
		}
		if (externalConnector.Shape == ConnectorProfileType.Rectangular && fittingConnector.Shape == ConnectorProfileType.Rectangular)
		{
			return Math.Abs(externalConnector.Height - fittingConnector.Height) < 0.0001 && Math.Abs(externalConnector.Width - fittingConnector.Width) < 0.0001;
		}
		if (externalConnector.Shape == ConnectorProfileType.Round && fittingConnector.Shape == ConnectorProfileType.Round)
		{
			return Math.Abs(externalConnector.Radius - fittingConnector.Radius) < 0.0001;
		}
		return true;
	}

	private static bool IsTapFitting(Element fitting)
	{
		Category category = fitting.Category;
		if (category != null && category.Id?.IntegerValue == -2008010 && fitting.Name.AsSpan().Contains("Tap".AsSpan(), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return fitting.Name.AsSpan().Contains("Tap".AsSpan(), StringComparison.OrdinalIgnoreCase);
	}

	private static bool AreParallelOrOpposite(XYZ v1, XYZ v2)
	{
		return Math.Abs(Math.Abs(v1.Normalize().DotProduct(v2.Normalize())) - 1.0) < 0.001;
	}

	private static bool AreConnectedOrCoincident(Connector first, Connector second)
	{
		if (first.Origin.DistanceTo(second.Origin) < 0.0001)
		{
			return true;
		}
		return first.AllRefs.Cast<Connector>().Any((Connector refConnector) => refConnector.Owner.Id == second.Owner.Id && refConnector.Origin.DistanceTo(second.Origin) < 0.0001);
	}

	private static bool IsExternallyConnected(Connector connector, ElementId ownerId)
	{
		return connector.AllRefs
			.Cast<Connector>()
			.Any(reference => reference?.Owner != null && reference.Owner.Id != ownerId);
	}

	private static double Average(Connector connector)
	{
		return (connector.Origin.X + connector.Origin.Y + connector.Origin.Z) / 3.0;
	}

	private static List<Connector> GetNonSurfaceConnectors(Element element)
	{
		ConnectorSet connectorSet = element.GetConnectors();
		if (connectorSet == null)
		{
			return new List<Connector>();
		}
		List<Connector> result = new List<Connector>();
		foreach (Connector connector in connectorSet)
		{
			if (connector.ConnectorType != ConnectorType.Surface)
			{
				result.Add(connector);
			}
		}
		return result;
	}
}
