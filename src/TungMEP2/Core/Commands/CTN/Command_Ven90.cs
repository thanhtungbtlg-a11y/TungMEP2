using System;
using System.Globalization;
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
public class Command_Ven90 : SecuredExternalCommand
{
	private sealed class PipeSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return element is Pipe;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private const double Epsilon = 1E-09;

	protected override void ExecuteCore()
	{
		try
		{
			Reference ventReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Chọn Pipe Vent");
			Reference connectedPipeReference = base.UiDocument.Selection.PickObject(ObjectType.PointOnElement, new PipeSelectionFilter(), "Chọn Pipe Kết Nối Với Vent");
			XYZ pointAprime = connectedPipeReference.GlobalPoint;
			Pipe pipe1 = base.Document.GetElement(ventReference) as Pipe;
			Pipe pipe2 = base.Document.GetElement(connectedPipeReference) as Pipe;
			if (pipe1 == null || pipe2 == null)
			{
				TaskDialog.Show("Ven90", "Phần tử được chọn phải là Pipe hợp lệ.");
				return;
			}
			if (!TryGetPipeLine(pipe1, out Line pipe1Line))
			{
				TaskDialog.Show("Ven90", "Không đọc được trục hình học của Pipe Vent.");
				return;
			}
			if (!TryGetPipeLine(pipe2, out Line pipe2Line))
			{
				TaskDialog.Show("Ven90", "Không đọc được trục hình học của Pipe kết nối.");
				return;
			}
			if (!TryGetProjectedDirection(pipe2Line, out XYZ pipe2DirectionOxy))
			{
				TaskDialog.Show("Ven90", "Pipe kết nối không có hướng chiếu OXY hợp lệ.");
				return;
			}
			Plane planeAtAprime = Plane.CreateByNormalAndOrigin(pipe2DirectionOxy, pointAprime);
			if (!IntersectUnboundLineWithPlane(pipe2Line, planeAtAprime, out XYZ pointA))
			{
				TaskDialog.Show("Ven90", "Không xác định được điểm A từ Pipe kết nối.");
				return;
			}
			if (!TryGetProjectedDirection(pipe1Line, out XYZ pipe1DirectionOxy))
			{
				TaskDialog.Show("Ven90", "Pipe Vent không có hướng chiếu OXY hợp lệ.");
				return;
			}
			Plane planeAtA = Plane.CreateByNormalAndOrigin(pipe1DirectionOxy, pointA);
			if (!IntersectUnboundLineWithPlane(pipe1Line, planeAtA, out XYZ pointB))
			{
				TaskDialog.Show("Ven90", "Không xác định được điểm B từ Pipe Vent.");
				return;
			}
			XYZ pointC = new XYZ(pointA.X, pointA.Y, pointB.Z);
			if (!IsValidPoint(pointA) || !IsValidPoint(pointB) || !IsValidPoint(pointC))
			{
				TaskDialog.Show("Ven90", "Dữ liệu hình học không hợp lệ để tính A, B, C.");
				return;
			}
			if (!TryResolveSharedDiameter(pipe1, out var sharedDiameter))
			{
				TaskDialog.Show("Ven90", "Không xác định được DN dùng chung cho 2 ống mới.");
				return;
			}
			if (!TryGetPipeTemplateIds(pipe1, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId))
			{
				TaskDialog.Show("Ven90", "Không đọc được thông tin template (System/PipeType/Level) từ Pipe Vent.");
				return;
			}
			double shortTolerance = Math.Max(base.Document.Application.ShortCurveTolerance, 1E-09);
			if (pointA.DistanceTo(pointC) <= shortTolerance || pointC.DistanceTo(pointB) <= shortTolerance)
			{
				TaskDialog.Show("Ven90", "Không thể tạo ống mới vì đoạn quá ngắn.");
				return;
			}
			using Transaction transaction = new Transaction(base.Document, "Ven90 - Create branch pipes");
			transaction.Start();
			Pipe pipeA = Pipe.Create(base.Document, systemTypeId, pipeTypeId, levelId, pointA, pointC);
			Pipe pipeB = Pipe.Create(base.Document, systemTypeId, pipeTypeId, levelId, pointC, pointB);
			if (!TrySetPipeDiameter(pipeA, sharedDiameter) || !TrySetPipeDiameter(pipeB, sharedDiameter))
			{
				transaction.RollBack();
				TaskDialog.Show("Ven90", "Không thể gán DN cho ống mới.");
				return;
			}
			TryConnect(delegate
			{
				pipeA.CreateElbow(pipeB);
			});
			if (!TryConnect(delegate
			{
				PipeUtilities.TeeMaster(pipe2, pipeA);
			}))
			{
				transaction.RollBack();
			}
			else
			{
				transaction.Commit();
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Ven90", ex2.ToString());
		}
	}

	private static bool TryGetPipeLine(Pipe pipe, out Line line)
	{
		line = null;
		if (!(pipe.Location is LocationCurve locationCurve))
		{
			return false;
		}
		if (!(locationCurve.Curve is Line pipeLine))
		{
			return false;
		}
		line = pipeLine;
		return true;
	}

	private static bool TryGetProjectedDirection(Line line, out XYZ directionOxy)
	{
		directionOxy = null;
		XYZ direction = line.Direction;
		XYZ projected = new XYZ(direction.X, direction.Y, 0.0);
		double projectedLength = projected.GetLength();
		if (projectedLength < 1E-09)
		{
			return false;
		}
		directionOxy = projected.Normalize();
		return true;
	}

	private static bool IntersectUnboundLineWithPlane(Line sourceLine, Plane plane, out XYZ point)
	{
		point = null;
		XYZ direction = sourceLine.Direction.Normalize();
		Line unboundLine = Line.CreateUnbound(sourceLine.GetEndPoint(0), direction);
		double denominator = plane.Normal.DotProduct(unboundLine.Direction);
		if (Math.Abs(denominator) < 1E-09)
		{
			return false;
		}
		double parameter = plane.Normal.DotProduct(plane.Origin - unboundLine.Origin) / denominator;
		point = unboundLine.Origin + unboundLine.Direction.Multiply(parameter);
		return true;
	}

	private static bool IsValidPoint(XYZ point)
	{
		return IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}

	private static bool TryResolveSharedDiameter(Pipe pipe1, out double sharedDiameter)
	{
		sharedDiameter = 0.0;
		if (!TryGetPipeDiameter(pipe1, out var pipe1Diameter))
		{
			return false;
		}
		if (!TryGetConfiguredVentDiameter(out var configuredVentDiameter))
		{
			return false;
		}
		if (Math.Abs(pipe1Diameter - configuredVentDiameter) <= 1E-09)
		{
			sharedDiameter = pipe1Diameter;
			return true;
		}
		TaskDialog taskDialog = new TaskDialog("Ven90")
		{
			MainInstruction = "DN Pipe Vent khác DNVent cấu hình",
			MainContent = "Chọn DN dùng chung cho Pipe A và Pipe B:",
			CommonButtons = TaskDialogCommonButtons.Cancel,
			AllowCancellation = true
		};
		taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "DN " + ToMillimeterText(configuredVentDiameter) + " (theo cấu hình DNVent)");
		taskDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DN " + ToMillimeterText(pipe1Diameter) + " (theo Pipe Vent)");
		switch (taskDialog.Show())
		{
		case TaskDialogResult.CommandLink1:
			sharedDiameter = configuredVentDiameter;
			return true;
		case TaskDialogResult.CommandLink2:
			sharedDiameter = pipe1Diameter;
			return true;
		default:
			return false;
		}
	}

	private static bool TryGetConfiguredVentDiameter(out double diameter)
	{
		diameter = 0.0;
		string rawValue = GetData.FromJson("config", "DNVent")?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}
		if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var diameterMm) && !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out diameterMm))
		{
			return false;
		}
		if (diameterMm <= 0.0)
		{
			return false;
		}
		diameter = UnitUtils.ConvertToInternalUnits(diameterMm, UnitTypeId.Millimeters);
		return IsFinite(diameter) && diameter > 1E-09;
	}

	private static bool TryGetPipeTemplateIds(Pipe templatePipe, out ElementId systemTypeId, out ElementId pipeTypeId, out ElementId levelId)
	{
		systemTypeId = ElementId.InvalidElementId;
		pipeTypeId = ElementId.InvalidElementId;
		levelId = ElementId.InvalidElementId;
		pipeTypeId = templatePipe.GetTypeId();
		if (pipeTypeId == ElementId.InvalidElementId)
		{
			return false;
		}
		systemTypeId = ((Element)templatePipe).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
		if (systemTypeId == ElementId.InvalidElementId)
		{
			return false;
		}
		levelId = templatePipe.ReferenceLevel?.Id ?? ElementId.InvalidElementId;
		if (levelId == ElementId.InvalidElementId)
		{
			return false;
		}
		return true;
	}

	private static bool TryGetPipeDiameter(Pipe pipe, out double diameter)
	{
		diameter = 0.0;
		Parameter diameterParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		if (diameterParameter == null)
		{
			return false;
		}
		diameter = diameterParameter.AsDouble();
		return IsFinite(diameter) && diameter > 1E-09;
	}

	private static bool TrySetPipeDiameter(Pipe pipe, double diameter)
	{
		Parameter diameterParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		if (diameterParameter == null || diameterParameter.IsReadOnly)
		{
			return false;
		}
		return diameterParameter.Set(diameter);
	}

	private static bool TryConnect(Action action)
	{
		try
		{
			action();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string ToMillimeterText(double diameterInternal)
	{
		double diameterMm = UnitUtils.ConvertFromInternalUnits(diameterInternal, UnitTypeId.Millimeters);
		return Math.Round(diameterMm, 2).ToString("0.##");
	}
}
