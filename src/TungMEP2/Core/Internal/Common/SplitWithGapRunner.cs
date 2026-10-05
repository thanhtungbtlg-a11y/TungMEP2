using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal static class SplitWithGapRunner
{
	private sealed class Runner
	{
		private const double PointTolerance = 1E-06;

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
			Reference firstReference = UiDocument.Selection.PickObject(ObjectType.Element, new SplitWithGapSelectionFilter(), "Chọn phần tử và điểm cắt thứ nhất");
			ElementId elementId = firstReference.ElementId;
			XYZ firstPickPoint = firstReference.GlobalPoint;
			Reference secondReference = UiDocument.Selection.PickObject(ObjectType.Element, new FixedElementSelectionFilter(elementId), "Chọn điểm cắt thứ hai trên cùng phần tử");
			if (secondReference.ElementId != elementId)
			{
				TaskDialog.Show("SplitWithGap", "Hai điểm phải nằm trên cùng 1 Element.");
				return;
			}
			Element sourceElement = Document.GetElement(elementId);
			if (!(sourceElement?.Location is LocationCurve sourceLocation))
			{
				TaskDialog.Show("SplitWithGap", "Phần tử không có đường tâm hợp lệ.");
				return;
			}
			if (!(sourceLocation.Curve is Line sourceLine))
			{
				TaskDialog.Show("SplitWithGap", "Lệnh chỉ hỗ trợ Element đường thẳng");
				return;
			}
			XYZ secondPickPoint = secondReference.GlobalPoint;
			XYZ firstProjectedPoint = sourceLine.Project(firstPickPoint)?.XYZPoint;
			XYZ secondProjectedPoint = sourceLine.Project(secondPickPoint)?.XYZPoint;
			if (firstProjectedPoint == null || secondProjectedPoint == null)
			{
				TaskDialog.Show("SplitWithGap", "Không thể xác định vị trí cắt trên phần tử");
				return;
			}
			XYZ startPoint = sourceLine.GetEndPoint(0);
			XYZ endPoint = sourceLine.GetEndPoint(1);
			XYZ nearStartPoint = GetNearestPoint(startPoint, firstProjectedPoint, secondProjectedPoint);
			XYZ nearEndPoint = GetNearestPoint(endPoint, firstProjectedPoint, secondProjectedPoint);
			if (nearStartPoint.DistanceTo(nearEndPoint) < 1E-06)
			{
				TaskDialog.Show("SplitWithGap", "Hai điểm cắt quá gần nhau.");
				return;
			}
			using TransactionGroup group = new TransactionGroup(Document, "Split With Gap");
			group.Start();
			using (Transaction transaction = new Transaction(Document, "Split With Gap"))
			{
				transaction.Start();
				List<FamilyInstance> disconnectedFittings = DisconnectConnectedFittings(sourceElement);
				sourceLocation.Curve = Line.CreateBound(nearEndPoint, endPoint);
				Element createdElement = CreateSegmentElement(sourceElement, startPoint, nearStartPoint);
				if (createdElement == null)
				{
					transaction.RollBack();
					TaskDialog.Show("SplitWithGap", "Elemnt này không được hỗ trợ");
					return;
				}
				Document.Regenerate();
				ReconnectFittings(disconnectedFittings, sourceElement, createdElement);
				transaction.Commit();
			}
			group.Assimilate();
		}

		private List<FamilyInstance> DisconnectConnectedFittings(Element sourceElement)
		{
			List<Connector> connectors = ConnectorUtilities.GetConnectors(sourceElement);
			List<FamilyInstance> fittings = new List<FamilyInstance>();
			if (connectors == null)
			{
				return fittings;
			}
			foreach (Connector connector in connectors)
			{
				if (connector.AllRefs == null)
				{
					continue;
				}
				foreach (Connector referenceConnector in connector.AllRefs)
				{
					if (connector.IsConnectedTo(referenceConnector) && !(referenceConnector.Owner.Id == sourceElement.Id))
					{
						if (referenceConnector.Owner is FamilyInstance fitting && !fittings.Contains(fitting))
						{
							fittings.Add(fitting);
						}
						connector.DisconnectFrom(referenceConnector);
					}
				}
			}
			return fittings;
		}

		private Element? CreateSegmentElement(Element sourceElement, XYZ startPoint, XYZ endPoint)
		{
			if (sourceElement is Conduit conduit)
			{
				Conduit createdConduit = Conduit.Create(Document, conduit.GetTypeId(), startPoint, endPoint, conduit.LevelId);
				CopyParameterValue(conduit, createdConduit, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
				return createdConduit;
			}
			if (sourceElement is CableTray cableTray)
			{
				CableTray createdTray = CableTray.Create(Document, cableTray.GetTypeId(), startPoint, endPoint, cableTray.LevelId);
				CopyParameterValue(cableTray, createdTray, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
				CopyParameterValue(cableTray, createdTray, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
				return createdTray;
			}
			if (sourceElement is Pipe pipe)
			{
				ElementId systemTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
				ElementId typeId = pipe.GetTypeId();
				ElementId levelId = pipe.ReferenceLevel.Id;
				Pipe createdPipe = Pipe.Create(Document, systemTypeId, typeId, levelId, startPoint, endPoint);
				CopyParameterValue(pipe, createdPipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
				CopyPipeInsulation(pipe, createdPipe);
				return createdPipe;
			}
			if (sourceElement is Duct duct)
			{
				ElementId systemTypeId2 = ((Element)duct).get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM).AsElementId();
				ElementId typeId2 = duct.GetTypeId();
				ElementId levelId2 = duct.ReferenceLevel.Id;
				Duct createdDuct = Duct.Create(Document, systemTypeId2, typeId2, levelId2, startPoint, endPoint);
				CopyDuctSize(duct, createdDuct);
				CopyDuctInsulation(duct, createdDuct);
				CopyDuctLining(duct, createdDuct);
				return createdDuct;
			}
			return null;
		}

		private void ReconnectFittings(IList<FamilyInstance> fittings, Element sourceElement, Element createdElement)
		{
			List<Connector> curveConnectors = new List<Connector>();
			curveConnectors.AddRange(GetAvailableEndConnectors(sourceElement));
			curveConnectors.AddRange(GetAvailableEndConnectors(createdElement));
			foreach (FamilyInstance fitting in fittings)
			{
				List<Connector> fittingConnectors = ConnectorUtilities.GetConnectors(fitting);
				if (fittingConnectors == null)
				{
					continue;
				}
				foreach (Connector fittingConnector in fittingConnectors.Where((Connector c) => c.ConnectorType == ConnectorType.End && !c.IsConnected).ToList())
				{
					Connector targetConnector = (from c in curveConnectors
						where !c.IsConnected && c.ConnectorType == ConnectorType.End && c.Domain == fittingConnector.Domain
						orderby c.Origin.DistanceTo(fittingConnector.Origin)
						select c).FirstOrDefault();
					if (targetConnector != null)
					{
						try
						{
							fittingConnector.ConnectTo(targetConnector);
						}
						catch
						{
						}
					}
				}
			}
		}

		private static List<Connector> GetAvailableEndConnectors(Element element)
		{
			List<Connector> connectors = ConnectorUtilities.GetConnectors(element);
			if (connectors == null)
			{
				return new List<Connector>();
			}
			return connectors.Where((Connector connector) => connector.ConnectorType == ConnectorType.End).ToList();
		}

		private static void CopyParameterValue(Element source, Element target, BuiltInParameter parameter)
		{
			Parameter sourceParameter = source.get_Parameter(parameter);
			Parameter targetParameter = target.get_Parameter(parameter);
			if (sourceParameter != null && targetParameter != null && sourceParameter.StorageType == targetParameter.StorageType)
			{
				switch (sourceParameter.StorageType)
				{
				case StorageType.Double:
					targetParameter.Set(sourceParameter.AsDouble());
					break;
				case StorageType.Integer:
					targetParameter.Set(sourceParameter.AsInteger());
					break;
				case StorageType.ElementId:
					targetParameter.Set(sourceParameter.AsElementId());
					break;
				case StorageType.String:
					targetParameter.Set(sourceParameter.AsString());
					break;
				}
			}
		}

		private void CopyPipeInsulation(Pipe sourcePipe, Pipe targetPipe)
		{
			PipeInsulation sourceInsulation = new FilteredElementCollector(Document).OfClass(typeof(PipeInsulation)).Cast<PipeInsulation>().FirstOrDefault((PipeInsulation insulation) => insulation.HostElementId == sourcePipe.Id);
			if (sourceInsulation != null)
			{
				PipeInsulation.Create(Document, targetPipe.Id, sourceInsulation.GetTypeId(), sourceInsulation.Thickness);
			}
		}

		private void CopyDuctSize(Duct sourceDuct, Duct targetDuct)
		{
			Parameter sourceDiameter = ((Element)sourceDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
			if (sourceDiameter != null && sourceDiameter.HasValue)
			{
				Parameter targetDiameter = ((Element)targetDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
				if (targetDiameter != null && !targetDiameter.IsReadOnly)
				{
					targetDiameter.Set(sourceDiameter.AsDouble());
					return;
				}
			}
			CopyParameterValue(sourceDuct, targetDuct, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
			CopyParameterValue(sourceDuct, targetDuct, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
		}

		private void CopyDuctInsulation(Duct sourceDuct, Duct targetDuct)
		{
			DuctInsulation sourceInsulation = new FilteredElementCollector(Document).OfClass(typeof(DuctInsulation)).Cast<DuctInsulation>().FirstOrDefault((DuctInsulation insulation) => insulation.HostElementId == sourceDuct.Id);
			if (sourceInsulation != null)
			{
				DuctInsulation.Create(Document, targetDuct.Id, sourceInsulation.GetTypeId(), sourceInsulation.Thickness);
			}
		}

		private void CopyDuctLining(Duct sourceDuct, Duct targetDuct)
		{
			DuctLining sourceLining = new FilteredElementCollector(Document).OfClass(typeof(DuctLining)).Cast<DuctLining>().FirstOrDefault((DuctLining lining) => lining.HostElementId == sourceDuct.Id);
			if (sourceLining != null)
			{
				DuctLining.Create(Document, targetDuct.Id, sourceLining.GetTypeId(), sourceLining.Thickness);
			}
		}

		private static XYZ GetNearestPoint(XYZ endpoint, XYZ firstPoint, XYZ secondPoint)
		{
			return (endpoint.DistanceTo(firstPoint) <= endpoint.DistanceTo(secondPoint)) ? firstPoint : secondPoint;
		}
	}

	private sealed class SplitWithGapSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			if (!(elem?.Location is LocationCurve))
			{
				return false;
			}
			return (elem is Pipe || elem is Duct || elem is Conduit || elem is CableTray) ? true : false;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
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

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Runner runner = new Runner(uiDocument, document);
		runner.Run();
	}
}
