using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal static class ConnectMasterRunner
{
	private sealed class Runner
	{
		private readonly UIDocument _uiDocument;

		private readonly Document _document;

		private UIDocument UiDocument => _uiDocument;

		private Document Document => _document;

		internal Runner(UIDocument uiDocument, Document document)
		{
			_uiDocument = uiDocument;
			_document = document;
		}

		internal void Run()
		{
			while (true)
			{
				ExecuteOnce();
			}
		}

		private void ExecuteOnce()
		{
			if (!TryGetElementsAndClickPoints(out Element element1, out XYZ pickPoint1, out Element element2, out XYZ pickPoint2))
			{
				return;
			}
			if (!TryGetConnectors(element1, element2, out List<Connector> connectors1, out List<Connector> _, out string error))
			{
				TaskDialog.Show("ConnectMaster", error ?? "Không tìm được connector hợp lệ.");
				return;
			}
			if (TryGetClosestConnectedPair(connectors1, element2, out Connector connected1, out Connector connected2))
			{
				using (Transaction tx = new Transaction(Document, "ConnectMaster (Disconnect)"))
				{
					tx.Start();
					connected1.DisconnectFrom(connected2);
					tx.Commit();
					return;
				}
			}
			if (element2.Pinned)
			{
				TaskDialog.Show("ConnectMaster", "Phần tử 2 đang bị pin, không thể dịch chuyển.");
				return;
			}
			if (!TryGetConnectorsByClickPoint(element1, pickPoint1, element2, pickPoint2, out Connector connector1, out Connector connector2, out error))
			{
				TaskDialog.Show("ConnectMaster", error ?? "Không tìm được connector phù hợp từ điểm click.");
				return;
			}
			XYZ moveVector = connector1.Origin - connector2.Origin;
			using Transaction tx2 = new Transaction(Document, "ConnectMaster (Connect)");
			tx2.Start();
			if (!TryAlignToOppositeStrict(connector1, connector2, out error))
			{
				TaskDialog.Show("ConnectMaster", error ?? "Không thể xoay phần tử để connector ngược chiều strict.");
				tx2.RollBack();
				return;
			}
			ElementTransformUtils.MoveElement(Document, element2.Id, moveVector);
			Document.Regenerate();
			if (!TryConnectWithNaviateRules(connector1, connector2, out error))
			{
				TaskDialog.Show("ConnectMaster", error ?? "Không thể kết nối hai connector.");
				tx2.RollBack();
			}
			else
			{
				tx2.Commit();
			}
		}

		private bool TryGetElementsAndClickPoints(out Element element1, out XYZ pickPoint1, out Element element2, out XYZ pickPoint2)
		{
			element1 = null;
			pickPoint1 = XYZ.Zero;
			element2 = null;
			pickPoint2 = XYZ.Zero;
			ICollection<ElementId> selectionIds = UiDocument.Selection.GetElementIds();
			if (selectionIds.Count == 2)
			{
				List<ElementId> ids = selectionIds.ToList();
				element1 = Document.GetElement(ids[0]);
				element2 = Document.GetElement(ids[1]);
				if (!TryPickPointOnElement(element1, "Click gần connector muốn dùng trên phần tử 1.", out pickPoint1) || !TryPickPointOnElement(element2, "Click gần connector muốn dùng trên phần tử 2.", out pickPoint2))
				{
					return false;
				}
				return true;
			}
			Isf.MepConnector filter = new Isf.MepConnector();
			Reference reference1 = UiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn phần tử 1 (mốc) - có connector");
			Reference reference2 = UiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn phần tử 2 (cần dịch chuyển và xoay) - có connector");
			element1 = Document.GetElement(reference1);
			pickPoint1 = GetPickPoint(reference1, element1);
			element2 = Document.GetElement(reference2);
			pickPoint2 = GetPickPoint(reference2, element2);
			return true;
		}

		private bool TryPickPointOnElement(Element element, string prompt, out XYZ pickPoint)
		{
			pickPoint = XYZ.Zero;
			Reference reference = UiDocument.Selection.PickObject(ObjectType.Element, new FixedElementSelectionFilter(element.Id), prompt);
			pickPoint = GetPickPoint(reference, element);
			return true;
		}

		private bool TryAlignToOppositeStrict(Connector stationaryConnector, Connector movingConnector, out string? error)
		{
			error = null;
			XYZ dir1 = stationaryConnector.CoordinateSystem.BasisZ.Normalize();
			XYZ dir2 = movingConnector.CoordinateSystem.BasisZ.Normalize();
			XYZ targetDir = dir1.Negate();
			double angle = dir2.AngleTo(targetDir);
			if (angle < 1E-06)
			{
				return true;
			}
			XYZ axisDirection = dir2.CrossProduct(targetDir);
			if (axisDirection.GetLength() < 1E-09)
			{
				axisDirection = movingConnector.CoordinateSystem.BasisY;
			}
			if (axisDirection.GetLength() < 1E-09)
			{
				axisDirection = movingConnector.CoordinateSystem.BasisX;
			}
			XYZ pivot = movingConnector.Origin;
			Line rotationAxis = Line.CreateBound(pivot, pivot + axisDirection.Multiply(10000000.0));
			ElementTransformUtils.RotateElement(Document, movingConnector.Owner.Id, rotationAxis, angle);
			Document.Regenerate();
			if (IsOppositeStrict(stationaryConnector, movingConnector))
			{
				return true;
			}
			ElementTransformUtils.RotateElement(Document, movingConnector.Owner.Id, rotationAxis, -2.0 * angle);
			Document.Regenerate();
			if (IsOppositeStrict(stationaryConnector, movingConnector))
			{
				return true;
			}
			error = "Không thể xoay phần tử 2 để hướng connector ngược chiều connector 1 (strict).";
			return false;
		}

		private bool TryConnectWithNaviateRules(Connector stationaryConnector, Connector movingConnector, out string? error)
		{
			error = null;
			try
			{
				if (stationaryConnector.Owner is FabricationPart && movingConnector.Owner is FabricationPart)
				{
					FabricationPart.ConnectAndCouple(Document, movingConnector, stationaryConnector);
				}
				else
				{
					movingConnector.ConnectTo(stationaryConnector);
				}
				return true;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}
	}

	private sealed class FixedElementSelectionFilter : ISelectionFilter
	{
		private readonly ElementId _elementId;

		public FixedElementSelectionFilter(ElementId elementId)
		{
			_elementId = elementId;
		}

		public bool AllowElement(Element elem)
		{
			return elem.Id == _elementId;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private const double AngleTolerance = 1E-06;

	private const double AxisTolerance = 1E-09;

	private const double OppositeAlignmentThreshold = 0.9999;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Runner runner = new Runner(uiDocument, document);
		runner.Run();
	}

	private static XYZ GetPickPoint(Reference reference, Element fallbackElement)
	{
		XYZ clickPoint = reference.GlobalPoint;
		if (clickPoint != null)
		{
			return clickPoint;
		}
		if (!(fallbackElement.Location is LocationPoint { Point: var point }))
		{
			if (fallbackElement.Location is LocationCurve locationCurve)
			{
				return locationCurve.Curve.Evaluate(0.5, normalized: true);
			}
			BoundingBoxXYZ boundingBox = fallbackElement.get_BoundingBox((View)null);
			if (boundingBox != null)
			{
				return (boundingBox.Min + boundingBox.Max) * 0.5;
			}
			return XYZ.Zero;
		}
		return point;
	}

	private static bool TryGetConnectors(Element element1, Element element2, out List<Connector> connectors1, out List<Connector> connectors2, out string? error)
	{
		error = null;
		connectors1 = ConnectorUtilities.GetConnectors(element1) ?? new List<Connector>();
		connectors2 = ConnectorUtilities.GetConnectors(element2) ?? new List<Connector>();
		if (connectors1.Count == 0)
		{
			error = "Phần tử 1 không có connector hợp lệ.";
			return false;
		}
		if (connectors2.Count == 0)
		{
			error = "Phần tử 2 không có connector hợp lệ.";
			return false;
		}
		return true;
	}

	private static bool TryGetClosestConnectedPair(IEnumerable<Connector> connectors1, Element element2, out Connector connector1, out Connector connector2)
	{
		connector1 = null;
		connector2 = null;
		double minDistance = double.MaxValue;
		foreach (Connector connector3 in connectors1)
		{
			if (!connector3.IsConnected)
			{
				continue;
			}
			foreach (Connector refConnector in connector3.AllRefs)
			{
				if (!(refConnector.Owner.Id != element2.Id))
				{
					double distance = (connector3.Origin - refConnector.Origin).GetLength();
					if (!(distance >= minDistance))
					{
						minDistance = distance;
						connector1 = connector3;
						connector2 = refConnector;
					}
				}
			}
		}
		return minDistance < double.MaxValue;
	}

	private static Connector[]? GetUnusedConnectors(Element element)
	{
		if (element is MEPCurve mepCurve)
		{
			return mepCurve.ConnectorManager?.UnusedConnectors?.Cast<Connector>().ToArray();
		}
		if (element is FamilyInstance familyInstance)
		{
			return familyInstance.MEPModel?.ConnectorManager?.UnusedConnectors?.Cast<Connector>().ToArray();
		}
		if (element is FabricationPart fabricationPart)
		{
			return fabricationPart.ConnectorManager?.UnusedConnectors?.Cast<Connector>().ToArray();
		}
		return null;
	}

	private static Connector[]? GetAllConnectors(Element element)
	{
		if (element is MEPCurve mepCurve)
		{
			return mepCurve.ConnectorManager?.Connectors?.Cast<Connector>().ToArray();
		}
		if (element is FamilyInstance familyInstance)
		{
			return familyInstance.MEPModel?.ConnectorManager?.Connectors?.Cast<Connector>().ToArray();
		}
		if (element is FabricationPart fabricationPart)
		{
			return fabricationPart.ConnectorManager?.Connectors?.Cast<Connector>().ToArray();
		}
		return null;
	}

	private static bool TryGetConnectorsByClickPoint(Element element1, XYZ pickPoint1, Element element2, XYZ pickPoint2, out Connector connector1, out Connector connector2, out string? error)
	{
		connector1 = null;
		connector2 = null;
		error = null;
		if (!TryPickUnconnectedConnectorByClickPoint(element1, pickPoint1, null, "Phần tử 1", out connector1, out error))
		{
			return false;
		}
		if (!TryPickUnconnectedConnectorByClickPoint(element2, pickPoint2, connector1.Domain, "Phần tử 2", out connector2, out error))
		{
			return false;
		}
		return true;
	}

	private static bool TryPickUnconnectedConnectorByClickPoint(Element element, XYZ pickPoint, Domain? requiredDomain, string elementName, out Connector connector, out string? error)
	{
		connector = null;
		error = null;
		Connector[] candidates = GetUnconnectedNonSurfaceConnectors(element, requiredDomain);
		if (candidates.Length == 0)
		{
			error = (requiredDomain.HasValue ? $"{elementName} không có connector trống cùng domain {requiredDomain.Value}." : (elementName + " không có connector trống hợp lệ."));
			return false;
		}
		connector = GetClosestConnector(candidates, pickPoint);
		if (connector == null)
		{
			error = "Không tìm được connector gần điểm click.";
			return false;
		}
		return true;
	}

	private static Connector[] GetUnconnectedNonSurfaceConnectors(Element element, Domain? requiredDomain)
	{
		Connector[] preferred = GetUnusedConnectors(element) ?? Array.Empty<Connector>();
		if (preferred.Length == 0)
		{
			preferred = GetAllConnectors(element) ?? Array.Empty<Connector>();
		}
		return FilterByDomainAndConnectivity(preferred, requiredDomain);
	}

	private static Connector[] FilterByDomainAndConnectivity(Connector[] connectors, Domain? requiredDomain)
	{
		IEnumerable<Connector> filtered = connectors;
		if (requiredDomain.HasValue)
		{
			filtered = filtered.Where((Connector c) => c.Domain == requiredDomain.Value);
		}
		filtered = filtered.Where((Connector c) => c != null && !c.IsConnected && !IsSurfaceConnectorSafe(c));
		return filtered.ToArray();
	}

	private static bool IsSurfaceConnectorSafe(Connector connector)
	{
		try
		{
			PropertyInfo property = connector.GetType().GetProperty("IsSurfaceConnector");
			if (property?.PropertyType != typeof(bool))
			{
				return false;
			}
			object value = property.GetValue(connector);
			bool isSurface = default(bool);
			int num;
			if (value is bool)
			{
				isSurface = (bool)value;
				num = 1;
			}
			else
			{
				num = 0;
			}
			return (byte)((uint)num & (isSurface ? 1u : 0u)) != 0;
		}
		catch
		{
			return false;
		}
	}

	private static Connector GetClosestConnector(IEnumerable<Connector> connectors, XYZ pickPoint)
	{
		Connector best = null;
		double bestDistance = double.MaxValue;
		foreach (Connector candidate in connectors)
		{
			double distance = candidate.Origin.DistanceTo(pickPoint);
			if (!(distance >= bestDistance))
			{
				bestDistance = distance;
				best = candidate;
			}
		}
		return best;
	}

	private static bool IsOppositeStrict(Connector stationaryConnector, Connector movingConnector)
	{
		XYZ dir1 = stationaryConnector.CoordinateSystem.BasisZ.Normalize();
		XYZ dir2 = movingConnector.CoordinateSystem.BasisZ.Normalize();
		double alignment = dir2.DotProduct(dir1.Negate());
		return alignment > 0.9999;
	}
}
