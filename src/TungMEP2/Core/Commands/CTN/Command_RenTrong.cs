using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_RenTrong : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			Reference picked = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.Fitting(), "Chọn Fitting");
			if (!(base.Document.GetElement(picked) is FamilyInstance fitting))
			{
				TaskDialog.Show("RenTrong", "Phần tử chọn không phải fitting hợp lệ.");
				return;
			}
			List<Connector> connectors = GetOrderedFittingConnectors(fitting);
			if (connectors.Count < 2)
			{
				TaskDialog.Show("RenTrong", "Fitting phải có ít nhất 2 connector piping.");
				return;
			}
			Connector connector0 = connectors[0];
			Connector connector1 = connectors[1];
			XYZ fittingPoint = (fitting.Location as LocationPoint)?.Point ?? connector0.Origin;
			XYZ normal = connector0.CoordinateSystem.BasisZ.Negate().Normalize() + connector1.CoordinateSystem.BasisZ.Normalize();
			if (normal.GetLength() < 1E-09)
			{
				TaskDialog.Show("RenTrong", "Không xác định được mặt phẳng mirror cho fitting.");
				return;
			}
			List<Pipe> connectedPipes = new List<Pipe>();
			using Transaction transaction = new Transaction(base.Document, "RenTrong");
			transaction.Start();
			DisconnectAndCollectPipes(connector0, connectedPipes);
			DisconnectAndCollectPipes(connector1, connectedPipes);
			Plane plane = Plane.CreateByNormalAndOrigin(normal.Normalize(), fittingPoint);
			ElementTransformUtils.MirrorElements(base.Document, new List<ElementId> { fitting.Id }, plane, mirrorCopies: false);
			if (connectedPipes.Count > 0)
			{
				foreach (Connector pipeConnector in connectedPipes[0].ConnectorManager.Connectors)
				{
					if (pipeConnector.CoordinateSystem.BasisZ.DotProduct(connector1.CoordinateSystem.BasisZ) < 0.0)
					{
						pipeConnector.ConnectTo(connector1);
						break;
					}
				}
			}
			if (connectedPipes.Count > 1)
			{
				Pipe sample = connectedPipes[0];
				ElementId systemTypeId = ((Element)sample).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
				if (systemTypeId == ElementId.InvalidElementId)
				{
					systemTypeId = sample.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
				}
				if (systemTypeId != ElementId.InvalidElementId)
				{
					XYZ start = connector0.Origin;
					XYZ end = start + connector0.CoordinateSystem.BasisZ.Normalize().Multiply(0.01);
					Pipe stubPipe = Pipe.Create(base.Document, systemTypeId, sample.GetTypeId(), sample.LevelId, start, end);
					double diameter = ((Element)sample).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
					((Element)stubPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(diameter);
					Connector stubStartConnector = ConnectorUtilities.GetClosestConnector(stubPipe, start);
					stubStartConnector.ConnectTo(connector0);
					List<Connector> nearest = stubPipe.GetConnectorCloset(connectedPipes[1]);
					if (nearest.Count == 2)
					{
						nearest[0].ConnectTo(nearest[1]);
					}
				}
			}
			XYZ jiggle = connector1.CoordinateSystem.BasisZ.Normalize().Multiply(0.001);
			ElementTransformUtils.MoveElement(base.Document, fitting.Id, jiggle);
			ElementTransformUtils.MoveElement(base.Document, fitting.Id, jiggle.Negate());
			transaction.Commit();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("RenTrong", ex2.ToString());
		}
	}

	private static List<Connector> GetOrderedFittingConnectors(FamilyInstance fitting)
	{
		ConnectorSet connectorSet = fitting.MEPModel?.ConnectorManager?.Connectors;
		if (connectorSet == null)
		{
			return new List<Connector>();
		}
		List<Connector> all = (from Connector connector in connectorSet
			where connector.Domain == Domain.DomainPiping && connector.ConnectorType == ConnectorType.End
			select connector).ToList();
		List<Connector> primary = all.Where(IsPrimaryConnector).ToList();
		List<Connector> nonPrimary = all.Where((Connector connector) => !IsPrimaryConnector(connector)).ToList();
		return primary.Concat(nonPrimary).ToList();
	}

	private static bool IsPrimaryConnector(Connector connector)
	{
		try
		{
			return connector.GetMEPConnectorInfo().IsPrimary;
		}
		catch
		{
			return false;
		}
	}

	private static void DisconnectAndCollectPipes(Connector fittingConnector, List<Pipe> connectedPipes)
	{
		foreach (Connector reference in fittingConnector.AllRefs)
		{
			Element owner = reference.Owner;
			Pipe pipe = owner as Pipe;
			if (pipe != null && connectedPipes.All((Pipe item) => item.Id != pipe.Id))
			{
				connectedPipes.Add(pipe);
			}
			if (!(reference.Owner.Id == fittingConnector.Owner.Id))
			{
				try
				{
					fittingConnector.DisconnectFrom(reference);
				}
				catch
				{
				}
			}
		}
	}
}
