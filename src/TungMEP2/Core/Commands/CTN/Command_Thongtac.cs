using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_Thongtac : SecuredExternalCommand
{
	private sealed class PipeOrFittingSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			if (elem is Pipe)
			{
				return true;
			}
			int result;
			if (elem is FamilyInstance familyInstance)
			{
				Category category = familyInstance.Category;
				if (category != null && category.Id?.Value == -2008049)
				{
					result = ((familyInstance.MEPModel?.ConnectorManager?.Connectors != null) ? 1 : 0);
					goto IL_007e;
				}
			}
			result = 0;
			goto IL_007e;
			IL_007e:
			return (byte)result != 0;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	protected override void ExecuteCore()
	{
		try
		{
			Reference pickRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeOrFittingSelectionFilter(), "Chọn Pipe hoặc Fitting");
			Element pickedElement = base.Document.GetElement(pickRef);
			if (pickedElement is FamilyInstance fitting)
			{
				ExecuteFromFitting(fitting, pickRef.GlobalPoint);
			}
			else if (pickedElement is Pipe pipe)
			{
				ExecuteFromPipe(pipe, pickRef.GlobalPoint);
			}
			else
			{
				TaskDialog.Show("CapEnd", "Phần tử chọn không hợp lệ.");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("CapEnd", ex2.ToString());
		}
	}

	private void ExecuteFromFitting(FamilyInstance fitting, XYZ pickPoint)
	{
		ConnectorSet connectors = fitting.MEPModel?.ConnectorManager?.Connectors;
		if (connectors == null)
		{
			TaskDialog.Show("CapEnd", "Fitting không có connector hợp lệ.");
			return;
		}
		Connector cMain = (from connector in connectors.Cast<Connector>().Where(IsCandidateMainConnector)
			orderby connector.Origin.DistanceTo(pickPoint)
			select connector).FirstOrDefault();
		if (cMain == null)
		{
			TaskDialog.Show("CapEnd", "Không tìm thấy đầu connector hở trên fitting.");
			return;
		}
		Pipe pipeSample = GetLargestConnectedPipe(connectors, fitting.Id);
		if (pipeSample == null)
		{
			TaskDialog.Show("CapEnd", "Không tìm thấy ống mẫu đang kết nối với fitting.");
			return;
		}
		if (!TryGetLengthsByPipe(pipeSample, out double l1Mm, out double l2Mm, out string error))
		{
			TaskDialog.Show("CapEnd", error);
			return;
		}
		XYZ vectorMain = cMain.CoordinateSystem.BasisZ.Normalize();
		XYZ vectorUp = GetSecondaryDirection(vectorMain);
		double cMainToP1 = UnitUtils.ConvertToInternalUnits(l1Mm * 2.2, UnitTypeId.Millimeters);
		double p1ToP2 = UnitUtils.ConvertToInternalUnits((l1Mm + l2Mm) * 1.1, UnitTypeId.Millimeters);
		XYZ p1 = cMain.Origin + vectorMain.Multiply(cMainToP1);
		XYZ p2 = p1 + vectorUp.Multiply(p1ToP2);
		ElementId systemTypeId = GetPipeSystemTypeId(pipeSample);
		if (systemTypeId == ElementId.InvalidElementId)
		{
			TaskDialog.Show("CapEnd", "Không lấy được System Type từ ống mẫu.");
			return;
		}
		double sampleDiameter = ((Element)pipeSample).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		using Transaction transaction = new Transaction(base.Document, "Thong tac - Fitting");
		transaction.Start();
		Pipe pipe1 = Pipe.Create(base.Document, pipeSample.GetTypeId(), pipeSample.LevelId, cMain, p1);
		Pipe pipe2 = Pipe.Create(base.Document, systemTypeId, pipeSample.GetTypeId(), pipeSample.LevelId, p1, p2);
		((Element)pipe1).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(sampleDiameter);
		((Element)pipe2).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(sampleDiameter);
		Connector pipe1Connector = ConnectorUtilities.GetClosestConnector(pipe1, p1);
		Connector pipe2Connector = ConnectorUtilities.GetClosestConnector(pipe2, p1);
		base.Document.Create.NewElbowFitting(pipe1Connector, pipe2Connector);
		PlumbingUtils.PlaceCapOnOpenEnds(base.Document, pipe2.Id, pipe2.PipeType.Id);
		transaction.Commit();
	}

	private void ExecuteFromPipe(Pipe selectedPipe, XYZ pickPoint)
	{
		Connector p1Connector = GetOpenPipeConnector(selectedPipe, pickPoint);
		if (p1Connector == null)
		{
			TaskDialog.Show("CapEnd", "Pipe không có đầu connector hở để chạy lệnh.");
			return;
		}
		if (!TryGetLengthsByPipe(selectedPipe, out double l1Mm, out double l2Mm, out string error))
		{
			TaskDialog.Show("CapEnd", error);
			return;
		}
		ElementId systemTypeId = GetPipeSystemTypeId(selectedPipe);
		if (systemTypeId == ElementId.InvalidElementId)
		{
			TaskDialog.Show("CapEnd", "Không lấy được System Type từ pipe đã chọn.");
			return;
		}
		XYZ p1 = p1Connector.Origin;
		XYZ vectorMain = p1Connector.CoordinateSystem.BasisZ.Normalize();
		XYZ vectorUp = GetSecondaryDirection(vectorMain);
		double p1ToP2 = UnitUtils.ConvertToInternalUnits((l1Mm + l2Mm) * 1.1, UnitTypeId.Millimeters);
		XYZ p2 = p1 + vectorUp.Multiply(p1ToP2);
		double sampleDiameter = ((Element)selectedPipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
		using Transaction transaction = new Transaction(base.Document, "Thong tac - Pipe");
		transaction.Start();
		Pipe pipe2 = Pipe.Create(base.Document, systemTypeId, selectedPipe.GetTypeId(), selectedPipe.LevelId, p1, p2);
		((Element)pipe2).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(sampleDiameter);
		Connector pipe2Connector = ConnectorUtilities.GetClosestConnector(pipe2, p1);
		base.Document.Create.NewElbowFitting(p1Connector, pipe2Connector);
		PlumbingUtils.PlaceCapOnOpenEnds(base.Document, pipe2.Id, pipe2.PipeType.Id);
		transaction.Commit();
	}

	private static bool IsCandidateMainConnector(Connector connector)
	{
		return connector.Domain == Domain.DomainPiping && connector.ConnectorType == ConnectorType.End && !connector.IsConnected;
	}

	private static Pipe? GetLargestConnectedPipe(ConnectorSet connectors, ElementId fittingId)
	{
		Pipe sample = null;
		double maxDiameter = -1.0;
		foreach (Connector connector in connectors)
		{
			if (!connector.IsConnected)
			{
				continue;
			}
			foreach (Connector reference in connector.AllRefs)
			{
				if (!(reference.Owner.Id == fittingId) && reference.Owner is Pipe pipe)
				{
					double diameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
					if (!(diameter <= maxDiameter))
					{
						maxDiameter = diameter;
						sample = pipe;
					}
				}
			}
		}
		return sample;
	}

	private static Connector? GetOpenPipeConnector(Pipe pipe, XYZ pickPoint)
	{
		List<Connector> openEnds = (from Connector connector in pipe.ConnectorManager.Connectors
			where connector.Domain == Domain.DomainPiping && connector.ConnectorType == ConnectorType.End && !connector.IsConnected
			select connector).ToList();
		if (openEnds.Count == 0)
		{
			return null;
		}
		if (openEnds.Count == 1)
		{
			return openEnds[0];
		}
		return openEnds.OrderBy((Connector connector) => connector.Origin.DistanceTo(pickPoint)).FirstOrDefault();
	}

	private static XYZ GetSecondaryDirection(XYZ vectorMain)
	{
		XYZ vectorUpRaw = vectorMain + XYZ.BasisZ;
		return (vectorUpRaw.GetLength() < 1E-09) ? XYZ.BasisX : vectorUpRaw.Normalize();
	}

	private static bool TryGetLengthsByPipe(Pipe pipe, out double l1Mm, out double l2Mm, out string error)
	{
		l1Mm = 0.0;
		l2Mm = 0.0;
		error = string.Empty;
		string dnKey = ((int)Math.Round(UnitUtils.ConvertFromInternalUnits(((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble(), UnitTypeId.Millimeters))).ToString(CultureInfo.InvariantCulture);
		if (!TryReadLengthMm("ElbowData", dnKey, out l1Mm, out string elbowError))
		{
			error = elbowError;
			return false;
		}
		if (!TryReadLengthMm("CapData", dnKey, out l2Mm, out string capError))
		{
			error = capError;
			return false;
		}
		return true;
	}

	private static bool TryReadLengthMm(string source, string key, out double valueMm, out string error)
	{
		valueMm = 0.0;
		error = string.Empty;
		string raw = GetData.FromJson(source, key);
		if (string.IsNullOrWhiteSpace(raw))
		{
			error = "Không tìm thấy dữ liệu '" + source + "' cho DN = " + key + " mm.";
			return false;
		}
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out valueMm))
		{
			error = "Dữ liệu '" + source + "' không hợp lệ cho DN = " + key + " mm: '" + raw + "'.";
			return false;
		}
		return true;
	}

	private static ElementId GetPipeSystemTypeId(Pipe pipe)
	{
		ElementId parameterTypeId = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM).AsElementId();
		if (parameterTypeId != ElementId.InvalidElementId)
		{
			return parameterTypeId;
		}
		return pipe.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
	}
}
