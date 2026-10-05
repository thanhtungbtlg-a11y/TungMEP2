using System;
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

internal static class ExtendFittingRunner
{
	private const double BaseLengthMm = 300.0;

	private const double DirectionTolerance = 1E-09;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Reference fittingRef = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Fitting(), "Chọn Fitting");
		FamilyInstance fitting = (FamilyInstance)document.GetElement(fittingRef);
		List<Connector> connectors = ConnectorUtilities.GetConnectors(fitting);
		if (connectors == null || connectors.Count == 0)
		{
			TaskDialog.Show("ExtendFitting", "Phụ kiện không có connector hợp lệ.");
			return;
		}
		List<Connector> unconnected = connectors.Where((Connector c) => c.ConnectorType == ConnectorType.End && !c.IsConnected).ToList();
		if (unconnected.Count == 0)
		{
			TaskDialog.Show("ExtendFitting", "Không có connector trống để kéo dài.");
			return;
		}
		Dictionary<Domain, Element> samples = GetSampleElements(fitting, connectors);
		foreach (Domain domain in unconnected.Select((Connector c) => c.Domain).Distinct())
		{
			if (!samples.ContainsKey(domain))
			{
				Reference sampleRef = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Sample(domain), GetSamplePrompt(domain));
				Element sample = document.GetElement(sampleRef);
				samples[domain] = sample;
			}
		}
		double baseLenInternal = UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters);
		using TransactionGroup group = new TransactionGroup(document, "Extend Fitting");
		group.Start();
		using (Transaction tx = new Transaction(document, "Extend Fitting"))
		{
			tx.Start();
			foreach (Connector connector in unconnected)
			{
				if (!samples.TryGetValue(connector.Domain, out var sample2))
				{
					TaskDialog.Show("ExtendFitting", "Thiếu phần tử mẫu cho miền tương ứng.");
					tx.RollBack();
					return;
				}
				XYZ direction = GetConnectorDirection(connector);
				if (direction.GetLength() < 1E-09)
				{
					continue;
				}
				ExtendFittingSize connectorSize = GetConnectorSize(connector);
				double size = connectorSize.ScaleSizeInternal;
				if (size <= 0.0)
				{
					size = GetScaleSize(sample2);
				}
				double length = ExtendFittingConnectorSizing.ComputeStubLengthInternal(baseLenInternal, size);
				XYZ start = connector.Origin;
				XYZ end = start + direction.Normalize().Multiply(length);
				Element created = CreateStubElement(sample2, start, end, connectorSize);
				if (created == null)
				{
					continue;
				}
				document.Regenerate();
				List<Connector> newConnectors = ConnectorUtilities.GetConnectors(created);
				if (newConnectors != null && newConnectors.Count != 0)
				{
					List<Connector> pair = ConnectorUtilities.GetConnectorClose(new List<Connector> { connector }, newConnectors.Where((Connector c) => c.ConnectorType == ConnectorType.End).ToList());
					pair?[0].ConnectTo(pair[1]);
				}
			}
			tx.Commit();
		}
		group.Assimilate();
	}

	private static Dictionary<Domain, Element> GetSampleElements(FamilyInstance fitting, List<Connector> connectors)
	{
		Dictionary<Domain, Element> samples = new Dictionary<Domain, Element>();
		foreach (Connector connector in connectors.Where((Connector c) => c.IsConnected))
		{
			foreach (Connector refConnector in connector.AllRefs)
			{
				if (!(refConnector.Owner.Id == fitting.Id))
				{
					Element owner = refConnector.Owner;
					if ((owner is MEPCurve || owner is CableTray) && !samples.ContainsKey(connector.Domain))
					{
						samples[connector.Domain] = owner;
					}
				}
			}
		}
		return samples;
	}

	private static string GetSamplePrompt(Domain domain)
	{
		if (1 == 0)
		{
		}
		string result = domain switch
		{
			Domain.DomainPiping => "Chọn Pipe mẫu", 
			Domain.DomainHvac => "Chọn ống gió mẫu", 
			Domain.DomainElectrical => "Chọn ống điện/máng cáp mẫu", 
			_ => "Chọn phần tử mẫu", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static XYZ GetConnectorDirection(Connector connector)
	{
		XYZ direction = connector.CoordinateSystem.BasisZ;
		if (direction.GetLength() < 1E-09)
		{
			direction = connector.CoordinateSystem.BasisX;
		}
		if (direction.GetLength() < 1E-09)
		{
			direction = connector.CoordinateSystem.BasisY;
		}
		return direction;
	}

	private static ExtendFittingSize GetConnectorSize(Connector connector)
	{
		if (connector.Shape == ConnectorProfileType.Round)
		{
			return ExtendFittingConnectorSizing.FromRound(connector.Radius);
		}
		if (connector.Shape == ConnectorProfileType.Rectangular || connector.Shape == ConnectorProfileType.Oval)
		{
			return ExtendFittingConnectorSizing.FromRect(connector.Width, connector.Height);
		}
		return new ExtendFittingSize(null, null, null);
	}

	private static double GetScaleSize(Element sample)
	{
		double? diameter = null;
		double? width = null;
		double? height = null;
		if (sample is Pipe pipe)
		{
			diameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		}
		else if (sample is Duct duct)
		{
			Parameter diameterParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
			if (diameterParam != null)
			{
				diameter = diameterParam.AsDouble();
			}
			Parameter widthParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
			if (widthParam != null)
			{
				width = widthParam.AsDouble();
			}
			Parameter heightParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
			if (heightParam != null)
			{
				height = heightParam.AsDouble();
			}
		}
		else if (sample is Conduit conduit)
		{
			diameter = ((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble();
		}
		else if (sample is CableTray cableTray)
		{
			width = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble();
			height = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble();
		}
		return ComputeScaleSize(diameter, width, height);
	}

	private static double ComputeStubLengthInternal(double baseLen, double size)
	{
		return Math.Max(baseLen, 3.0 * size);
	}

	private static double ComputeScaleSize(double? diameter, double? width, double? height)
	{
		if (diameter.HasValue)
		{
			return diameter.Value;
		}
		double max = 0.0;
		if (width.HasValue && width.Value > max)
		{
			max = width.Value;
		}
		if (height.HasValue && height.Value > max)
		{
			max = height.Value;
		}
		return max;
	}

	private static Element? CreateStubElement(Element sample, XYZ start, XYZ end, ExtendFittingSize connectorSize)
	{
		Document document = sample.Document;
		if (sample is Pipe pipe)
		{
			ElementId systemTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
			ElementId pipeTypeId = pipe.GetTypeId();
			ElementId levelId = pipe.ReferenceLevel.Id;
			Pipe newPipe = Pipe.Create(document, systemTypeId, pipeTypeId, levelId, start, end);
			double sampleDiameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
			double diameter = connectorSize.DiameterInternal ?? sampleDiameter;
			Parameter diameterParameter = ((Element)newPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
			if (diameterParameter != null && !diameterParameter.IsReadOnly && diameter > 0.0)
			{
				diameterParameter.Set(diameter);
			}
			return newPipe;
		}
		if (sample is Duct duct)
		{
			ElementId systemTypeId2 = ((Element)duct).get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM).AsElementId();
			ElementId ductTypeId = duct.GetTypeId();
			ElementId levelId2 = duct.ReferenceLevel.Id;
			Duct newDuct = Duct.Create(document, systemTypeId2, ductTypeId, levelId2, start, end);
			Parameter sampleDiameterParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
			Parameter sampleWidthParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
			Parameter sampleHeightParam = ((Element)duct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
			double? diameter2 = connectorSize.DiameterInternal;
			if (!diameter2.HasValue && sampleDiameterParam != null)
			{
				diameter2 = sampleDiameterParam.AsDouble();
			}
			double? width = connectorSize.WidthInternal;
			if (!width.HasValue && sampleWidthParam != null)
			{
				width = sampleWidthParam.AsDouble();
			}
			double? height = connectorSize.HeightInternal;
			if (!height.HasValue && sampleHeightParam != null)
			{
				height = sampleHeightParam.AsDouble();
			}
			Parameter newDiameterParam = ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
			if (newDiameterParam != null && !newDiameterParam.IsReadOnly && diameter2.HasValue && diameter2.Value > 0.0)
			{
				newDiameterParam.Set(diameter2.Value);
			}
			Parameter newWidthParam = ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
			if (newWidthParam != null && !newWidthParam.IsReadOnly && width.HasValue && width.Value > 0.0)
			{
				newWidthParam.Set(width.Value);
			}
			Parameter newHeightParam = ((Element)newDuct).get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
			if (newHeightParam != null && !newHeightParam.IsReadOnly && height.HasValue && height.Value > 0.0)
			{
				newHeightParam.Set(height.Value);
			}
			return newDuct;
		}
		if (sample is Conduit { LevelId: var levelId3 } conduit)
		{
			Conduit newConduit = Conduit.Create(document, conduit.GetTypeId(), start, end, levelId3);
			double sampleDiameter2 = ((Element)conduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble();
			double diameter3 = connectorSize.DiameterInternal ?? sampleDiameter2;
			Parameter diameterParameter2 = ((Element)newConduit).get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
			if (diameterParameter2 != null && !diameterParameter2.IsReadOnly && diameter3 > 0.0)
			{
				diameterParameter2.Set(diameter3);
			}
			return newConduit;
		}
		if (sample is CableTray { LevelId: var levelId4 } cableTray)
		{
			CableTray newTray = CableTray.Create(document, cableTray.GetTypeId(), start, end, levelId4);
			double sampleWidth = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble();
			double sampleHeight = ((Element)cableTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble();
			double width2 = connectorSize.WidthInternal ?? sampleWidth;
			double height2 = connectorSize.HeightInternal ?? sampleHeight;
			Parameter widthParameter = ((Element)newTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
			if (widthParameter != null && !widthParameter.IsReadOnly && width2 > 0.0)
			{
				widthParameter.Set(width2);
			}
			Parameter heightParameter = ((Element)newTray).get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
			if (heightParameter != null && !heightParameter.IsReadOnly && height2 > 0.0)
			{
				heightParameter.Set(height2);
			}
			return newTray;
		}
		return null;
	}
}
