using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal readonly record struct MepPipeBodyProjectionCandidate(
	int ConnectorIndex,
	XYZ ConnectorOrigin,
	XYZ InsertionPoint,
	double Distance);

internal sealed class MepPipeBodyConnectionPlan
{
	internal Pipe MainPipe { get; }

	internal Pipe BranchPipe { get; }

	internal XYZ OriginalBranchConnectorOrigin { get; }

	internal XYZ InsertionPoint { get; }

	internal MepPipeBodyConnectionPlan(
		Pipe mainPipe,
		Pipe branchPipe,
		XYZ originalBranchConnectorOrigin,
		XYZ insertionPoint)
	{
		MainPipe = mainPipe;
		BranchPipe = branchPipe;
		OriginalBranchConnectorOrigin = originalBranchConnectorOrigin;
		InsertionPoint = insertionPoint;
	}
}

internal static class MepPipeBodyConnectionService
{
	private const double PositionTolerance = 1E-05;

	internal static bool TryChooseCandidate(
		IReadOnlyList<MepPipeBodyProjectionCandidate> candidates,
		out MepPipeBodyProjectionCandidate candidate)
	{
		candidate = default;
		if (candidates == null || candidates.Count == 0)
		{
			return false;
		}

		candidate = candidates
			.OrderBy(item => item.Distance)
			.ThenBy(item => item.ConnectorIndex)
			.First();
		return true;
	}

	internal static bool HasThreeConnectedEnds(
		IReadOnlyList<bool> connectedEndStates)
	{
		return connectedEndStates != null &&
		       connectedEndStates.Count(state => state) >= 3;
	}

	internal static bool TryCreatePlan(
		Document document,
		Pipe mainPipe,
		Element movingSelectedElement,
		out MepPipeBodyConnectionPlan plan,
		out string error)
	{
		plan = null;
		error = string.Empty;
		if (document == null || mainPipe == null || movingSelectedElement == null)
		{
			error = "Không đọc được Pipe mốc hoặc Pipe cần di chuyển.";
			return false;
		}

		if (movingSelectedElement is not Pipe branchPipe)
		{
			error = "Nối vào thân chỉ hỗ trợ khi cả phần tử mốc và phần tử thứ hai đều là Pipe.";
			return false;
		}

		if (mainPipe.Id == branchPipe.Id)
		{
			error = "Ống mốc và ống nhánh phải là hai phần tử khác nhau.";
			return false;
		}

		if (mainPipe.Location is not LocationCurve mainLocation ||
		    mainLocation.Curve is not Line mainLine ||
		    branchPipe.Location is not LocationCurve branchLocation ||
		    branchLocation.Curve is not Line branchLine)
		{
			error = "MC-Align chỉ có thể chèn Tee/Y vào đoạn Pipe thẳng.";
			return false;
		}

		if (MepPipeBodyConnectionGeometry.AreParallel(
				mainLine.Direction,
				branchLine.Direction))
		{
			error = "Ống nhánh đang song song với ống mốc nên không thể tạo Tee/Y an toàn.";
			return false;
		}

		List<Connector> openBranchEnds = MepConnectedClusterCollector
			.GetPhysicalConnectors(branchPipe)
			.Where(connector =>
				connector.ConnectorType == ConnectorType.End &&
				connector.Domain == Domain.DomainPiping &&
				!connector.IsConnected)
			.ToList();
		if (openBranchEnds.Count == 0)
		{
			error = "Pipe được chọn thứ hai không còn đầu hở để nối vào thân ống mốc.";
			return false;
		}

		double mainDiameter = GetDiameter(mainPipe);
		double branchDiameter = GetDiameter(branchPipe);
		double endClearance = Math.Max(
			document.Application.ShortCurveTolerance * 2.0,
			Math.Max(mainDiameter, branchDiameter));
		List<MepPipeBodyProjectionCandidate> candidates =
			new List<MepPipeBodyProjectionCandidate>();
		for (int index = 0; index < openBranchEnds.Count; index++)
		{
			Connector connector = openBranchEnds[index];
			if (!MepPipeBodyConnectionGeometry.TryProjectInteriorPoint(
					mainLine.GetEndPoint(0),
					mainLine.GetEndPoint(1),
					connector.Origin,
					endClearance,
					out XYZ insertionPoint))
			{
				continue;
			}

			candidates.Add(new MepPipeBodyProjectionCandidate(
				index,
				connector.Origin,
				insertionPoint,
				connector.Origin.DistanceTo(insertionPoint)));
		}

		if (!TryChooseCandidate(candidates, out MepPipeBodyProjectionCandidate selected))
		{
			error = "Không tìm được vị trí đủ khoảng trống trên thân ống mốc để đặt Tee/Y.";
			return false;
		}

		plan = new MepPipeBodyConnectionPlan(
			mainPipe,
			branchPipe,
			selected.ConnectorOrigin,
			selected.InsertionPoint);
		return true;
	}

	internal static bool TryExecute(
		Document document,
		MepPipeBodyConnectionPlan plan,
		ICollection<ElementId> movingElementIds,
		out FamilyInstance junction,
		out string error)
	{
		junction = null;
		error = string.Empty;
		if (document == null || plan == null ||
		    movingElementIds == null || movingElementIds.Count == 0)
		{
			error = "Thiếu dữ liệu để di chuyển cụm và chèn Tee/Y.";
			return false;
		}

		try
		{
			XYZ move = plan.InsertionPoint - plan.OriginalBranchConnectorOrigin;
			if (move.GetLength() > 1E-06)
			{
				ElementTransformUtils.MoveElements(
					document,
					movingElementIds,
					move);
				document.Regenerate();
			}

			Connector movedBranchConnector = MepConnectedClusterCollector
				.GetPhysicalConnectors(plan.BranchPipe)
				.Where(connector =>
					connector.ConnectorType == ConnectorType.End &&
					connector.Domain == Domain.DomainPiping &&
					!connector.IsConnected)
				.OrderBy(connector =>
					connector.Origin.DistanceTo(plan.InsertionPoint))
				.FirstOrDefault();
			if (movedBranchConnector == null ||
			    movedBranchConnector.Origin.DistanceTo(plan.InsertionPoint) >
			    PositionTolerance)
			{
				error = "Không thể đưa đầu hở của nhánh tới đúng vị trí trên thân ống mốc.";
				return false;
			}

			junction = PipeUtilities.TeeMaster(
				plan.MainPipe,
				plan.BranchPipe);
			document.Regenerate();
			List<bool> connectedEndStates = junction?.MEPModel?
				.ConnectorManager?.Connectors?
				.Cast<Connector>()
				.Where(connector =>
					connector.ConnectorType == ConnectorType.End)
				.Select(connector => connector.IsConnected)
				.ToList() ?? new List<bool>();
			if (!HasThreeConnectedEnds(connectedEndStates))
			{
				error = "Tee/Y đã được tạo nhưng chưa kết nối đủ ba nhánh.";
				return false;
			}

			return true;
		}
		catch (Exception exception)
		{
			error =
				"Không thể chèn Tee/Y vào thân ống. Kiểm tra Junction/Transition trong Routing Preferences, " +
				"góc nối, kích thước fitting và chiều dài đoạn ống. Chi tiết: " +
				exception.Message;
			return false;
		}
	}

	private static double GetDiameter(Pipe pipe)
	{
		return pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
			?.AsDouble() ?? 0.0;
	}
}
