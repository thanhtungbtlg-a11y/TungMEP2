using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Common;

internal static class FitRotateRunner
{
	private sealed class ExcludingSelectionFilter : ISelectionFilter
	{
		private readonly ISelectionFilter _baseFilter;

		private readonly HashSet<ElementId> _excludedElementIds;

		public ExcludingSelectionFilter(ISelectionFilter baseFilter, IEnumerable<ElementId> excludedElementIds)
		{
			_baseFilter = baseFilter;
			_excludedElementIds = excludedElementIds.ToHashSet();
		}

		public bool AllowElement(Element elem)
		{
			if (elem == null || _excludedElementIds.Contains(elem.Id))
			{
				return false;
			}
			return _baseFilter.AllowElement(elem);
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return _baseFilter.AllowReference(reference, position);
		}
	}

	private const string DialogTitle = "FitRotate";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<ElementId> rotateElementIds = GetPreselectedRotatableElementIds(uiDocument, document);
		if (TryResolveRotateSetAndAxis(uiDocument, document, rotateElementIds, out ICollection<ElementId> idsToRotate, out Line axisLine))
		{
			if (axisLine == null)
			{
				TaskDialog.Show(DialogTitle, "Không xác định được trục xoay.");
				return;
			}
			if (idsToRotate.Any((ElementId id) => document.GetElement(id)?.Pinned ?? false))
			{
				TaskDialog.Show(DialogTitle, "Có phần tử đang bị Pin. Hãy bỏ Pin trước khi xoay.");
				return;
			}
			UI_FitRotateWindow window = new UI_FitRotateWindow(document, axisLine, idsToRotate);
			new WindowInteropHelper(window)
			{
				Owner = uiDocument.Application.MainWindowHandle
			};
			window.ShowDialog();
			List<ElementId> survivingIds = idsToRotate
				.Where(id => document.GetElement(id) != null)
				.ToList();
			if (survivingIds.Count > 0)
			{
				uiDocument.Selection.SetElementIds(survivingIds);
			}
		}
	}

	private static bool TryResolveRotateSetAndAxis(UIDocument uiDocument, Document document, List<ElementId> preselectedIds, out ICollection<ElementId> idsToRotate, out Line? axisLine)
	{
		idsToRotate = new List<ElementId>();
		axisLine = null;
		if (preselectedIds.Count >= 2)
		{
			idsToRotate = preselectedIds;
			Reference axisReference = uiDocument.Selection.PickObject(
				ObjectType.Element,
				new ExcludingSelectionFilter(new Isf.StraightSegment(), preselectedIds),
				"Chọn đoạn ống/ống gió thẳng đang nối trực tiếp với cụm và phải đứng yên");
			Element axisElement = document.GetElement(axisReference);
			if (!FitRotateAxisResolver.TryGetAxisLineFromConnectedSegment(axisElement, idsToRotate, document, out axisLine, out string connectedAxisError))
			{
				TaskDialog.Show(DialogTitle, connectedAxisError);
				return false;
			}
			if (!TryValidateRotationBoundary(document, idsToRotate, axisElement, out string boundaryError))
			{
				TaskDialog.Show(DialogTitle, boundaryError);
				return false;
			}
			return true;
		}
		Element fitting = GetSingleFitting(uiDocument, document, preselectedIds);
		idsToRotate = new List<ElementId> { fitting.Id };
		int connectedEndCount = CountExternallyConnectedEnds(fitting);
		if (connectedEndCount > 1)
		{
			TaskDialog.Show(
				DialogTitle,
				$"Fitting đang nối kín {connectedEndCount} đầu nên Revit không thể xoay riêng fitting đó.\n\n" +
				"Cách chọn đúng:\n" +
				"1. Chọn fitting cùng toàn bộ ống/fitting của nhánh cần di chuyển.\n" +
				"2. Bấm FitRotate.\n" +
				"3. Chọn đoạn ống thẳng nối trực tiếp với cụm và phải đứng yên để làm trục.");
			return false;
		}
		if (FitRotateAxisResolver.TryGetAxisLineFromSingleFittingConnectors(fitting, out axisLine, out string autoAxisError))
		{
			return true;
		}
		Reference axisReferenceFallback = uiDocument.Selection.PickObject(
			ObjectType.Element,
			new Isf.StraightSegment(),
			$"Không tự động suy ra được trục ({autoAxisError}). Hãy chọn segment thẳng làm trục xoay");
		Element axisElementFallback = document.GetElement(axisReferenceFallback);
		if (!FitRotateAxisResolver.TryGetAxisLineFromStraightSegment(axisElementFallback, out axisLine, out string straightAxisError))
		{
			TaskDialog.Show(DialogTitle, straightAxisError);
			return false;
		}
		return true;
	}

	private static Element GetSingleFitting(UIDocument uiDocument, Document document, IReadOnlyCollection<ElementId> preselectedIds)
	{
		if (preselectedIds.Count == 1)
		{
			Element preselectedElement = document.GetElement(preselectedIds.First());
			if (new Isf.Fitting().AllowElement(preselectedElement))
			{
				return preselectedElement;
			}
		}
		Reference fitReference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Fitting(), "Chọn fitting cần xoay");
		return document.GetElement(fitReference);
	}

	private static List<ElementId> GetPreselectedRotatableElementIds(UIDocument uiDocument, Document document)
	{
		return (from id in uiDocument.Selection.GetElementIds()
			where IsRotatableElement(document.GetElement(id))
			select id).ToList();
	}

	private static bool IsRotatableElement(Element element)
	{
		return new Isf.Fitting().AllowElement(element) || new Isf.StraightSegment().AllowElement(element);
	}

	private static bool TryValidateRotationBoundary(
		Document document,
		ICollection<ElementId> rotateElementIds,
		Element axisElement,
		out string error)
	{
		error = string.Empty;
		HashSet<ElementId> rotateIds = rotateElementIds.ToHashSet();
		List<Connector> selectedConnectors = rotateIds
			.Select(document.GetElement)
			.Where(element => element != null)
			.SelectMany(GetPhysicalConnectors)
			.ToList();
		List<Connector> axisConnectors = GetPhysicalConnectors(axisElement);

		bool touchesAxis = selectedConnectors.Any(selectedConnector =>
			axisConnectors.Any(axisConnector =>
				axisConnector.Origin.DistanceTo(selectedConnector.Origin) < 0.0001 ||
				AreDirectlyConnected(selectedConnector, axisConnector)));
		if (!touchesAxis)
		{
			error =
				"Đoạn làm trục không nối trực tiếp với cụm đã chọn.\n\n" +
				"Hãy chọn đúng đoạn ống thẳng đang nối vào fitting/nhánh cần xoay.";
			return false;
		}

		HashSet<ElementId> externalOwnerIds = new HashSet<ElementId>();
		foreach (Connector connector in selectedConnectors)
		{
			foreach (Connector reference in connector.AllRefs.Cast<Connector>())
			{
				Element owner = reference?.Owner;
				if (owner == null ||
					owner.Id == connector.Owner.Id ||
					owner.Id == axisElement.Id ||
					rotateIds.Contains(owner.Id) ||
					reference.ConnectorType == ConnectorType.Logical)
				{
					continue;
				}
				externalOwnerIds.Add(owner.Id);
			}
		}

		if (externalOwnerIds.Count == 0)
		{
			return true;
		}

		List<string> externalDescriptions = externalOwnerIds
			.Take(6)
			.Select(id =>
			{
				Element element = document.GetElement(id);
				string category = element?.Category?.Name ?? "Không rõ category";
				string name = element?.Name ?? "Không rõ tên";
				return $"• {category} — {name} — ID {id.IntegerValue}";
			})
			.ToList();
		string more = externalOwnerIds.Count > externalDescriptions.Count
			? $"\n• ... và {externalOwnerIds.Count - externalDescriptions.Count} phần tử khác"
			: string.Empty;
		error =
			"Cụm cần xoay vẫn còn nối với phần tử nằm ngoài vùng chọn (không phải đoạn trục). " +
			"Nếu xoay ngay, Revit sẽ khóa fitting hoặc kéo sai nhánh.\n\n" +
			"Hãy chọn thêm toàn bộ nhánh cần di chuyển, nhưng không chọn đoạn trục cố định:\n" +
			string.Join("\n", externalDescriptions) +
			more;
		return false;
	}

	private static int CountExternallyConnectedEnds(Element element)
	{
		return GetPhysicalConnectors(element)
			.Count(connector => connector.AllRefs
				.Cast<Connector>()
				.Any(reference =>
					reference?.Owner != null &&
					reference.Owner.Id != element.Id &&
					reference.ConnectorType != ConnectorType.Logical));
	}

	private static bool AreDirectlyConnected(Connector first, Connector second)
	{
		return first.AllRefs
			.Cast<Connector>()
			.Any(reference =>
				reference?.Owner != null &&
				reference.Owner.Id == second.Owner.Id &&
				reference.Origin.DistanceTo(second.Origin) < 0.0001);
	}

	private static List<Connector> GetPhysicalConnectors(Element element)
	{
		ConnectorSet connectorSet = element?.GetConnectors();
		if (connectorSet == null)
		{
			return new List<Connector>();
		}

		List<Connector> connectors = new List<Connector>();
		foreach (Connector connector in connectorSet)
		{
			if (connector.ConnectorType != ConnectorType.Surface &&
				connector.ConnectorType != ConnectorType.Logical)
			{
				connectors.Add(connector);
			}
		}
		return connectors;
	}
}
