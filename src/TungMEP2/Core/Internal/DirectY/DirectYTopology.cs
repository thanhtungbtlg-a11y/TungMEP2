using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal sealed class DirectYTail
{
	internal List<ElementId> MovingIds = new List<ElementId>();
	internal List<FitSpaceConnectorBinding> Bindings = new List<FitSpaceConnectorBinding>();
	internal FamilyInstance Elbow;
	internal Pipe Anchor;
	internal Connector ElbowNear, MovingBoundary, AnchorNear;
}

internal static class DirectYTopology
{
	internal static bool TryCollectTail(Document document, Pipe source, Connector sourceNear,
		out DirectYTail tail, out string error)
	{
		tail = new DirectYTail(); error = string.Empty;
		tail.MovingIds.Add(source.Id);
		HashSet<long> visited = new HashSet<long> { source.Id.Value };
		Connector outgoing = sourceNear;
		for (int step = 0; step < 32; step++)
		{
			if (!SinglePeer(outgoing, out Connector incoming) || !visited.Add(incoming.Owner.Id.Value))
			{ error = "Nhánh bị hở ở giữa, có nhiều kết nối hoặc tạo vòng trước khi tới elbow."; return false; }
			Element next = incoming.Owner;
			var ports = Ports(next);
			if (ports.Count != 2) { error = "Nhánh gặp Tee/thiết bị hoặc fitting nhiều cổng trước elbow; cần chọn đoạn nhánh gần đầu hở hơn."; return false; }
			tail.Bindings.Add(Bind(outgoing, incoming)); tail.MovingIds.Add(next.Id);
			Connector other = ports.Single(port => port.Id != incoming.Id);
			if (IsElbow(next, out FamilyInstance elbow))
			{
				tail.Elbow = elbow; tail.ElbowNear = incoming;
				outgoing = other;
				for (int inlineStep = 0; inlineStep < 12; inlineStep++)
				{
					if (!SinglePeer(outgoing, out incoming) || visited.Contains(incoming.Owner.Id.Value))
					{ error = "Không tìm được Pipe thẳng phía sau elbow để làm trục."; return false; }
					if (incoming.Owner is Pipe anchor && IsStraightPipe(anchor))
					{ tail.Anchor = anchor; tail.AnchorNear = incoming; tail.MovingBoundary = outgoing; return true; }
					if (!IsInline(incoming.Owner))
					{ error = "Phía sau elbow phải là Pipe hoặc chuỗi fitting thẳng tới Pipe."; return false; }
					visited.Add(incoming.Owner.Id.Value); tail.MovingIds.Add(incoming.Owner.Id);
					tail.Bindings.Add(Bind(outgoing, incoming));
					outgoing = Ports(incoming.Owner).Single(port => port.Id != incoming.Id);
				}
				break;
			}
			if (!IsInline(next)) { error = "Fitting trung gian phải là con thu/union thẳng hoặc Pipe thẳng."; return false; }
			outgoing = other;
		}
		error = "Chuỗi nhánh quá dài hoặc chưa tìm thấy elbow trong phạm vi 32 phần tử."; return false;
	}

	internal static bool TryFindY(Pipe mainPipe, Pipe source, out FamilyInstance y, out string error, out bool mayCreate)
	{
		y = null; error = string.Empty; mayCreate = false;
		if (mainPipe == null || source == null || !IsStraightPipe(mainPipe) || mainPipe.Id == source.Id)
		{ error = "Chọn Pipe tuyến chính khác Pipe nhánh, có tim thẳng."; return false; }
		var sourceOpen = Ports(source).Where(port => !port.IsConnected).ToArray();
		if (sourceOpen.Length != 1) { error = "Pipe nhánh phải có đúng một đầu hở."; return false; }
		Line mainLine = (Line)((LocationCurve)mainPipe.Location).Curve;
		XYZ mainAxis = mainLine.Direction.Normalize();
		Queue<Element> queue = new Queue<Element>(); queue.Enqueue(mainPipe);
		HashSet<long> visited = new HashSet<long>();
		List<(FamilyInstance Y, double Distance, double BranchDot)> candidates = new List<(FamilyInstance, double, double)>();
		while (queue.Count > 0 && visited.Count < 128)
		{
			Element current = queue.Dequeue();
			if (!visited.Add(current.Id.Value)) continue;
			IReadOnlyList<Connector> through = Ports(current);
			if (IsY(current))
			{
				if (!TryMainPorts(through, out var runPorts, out Connector branch)) continue;
				if (Math.Abs(runPorts[0].CoordinateSystem.BasisZ.Normalize().DotProduct(mainAxis)) < 0.98) continue;
				through = runPorts;
				if (!branch.IsConnected && runPorts.All(port => SinglePeer(port, out _)) &&
					branch.Shape == ConnectorProfileType.Round &&
					Math.Abs(branch.Radius - sourceOpen[0].Radius) <= 0.1 / 304.8)
				{
					candidates.Add(((FamilyInstance)current, branch.Origin.DistanceTo(sourceOpen[0].Origin),
						branch.CoordinateSystem.BasisZ.Normalize().DotProduct(runPorts[0].CoordinateSystem.BasisZ.Normalize())));
				}
			}
			else if (!IsInline(current) || through.Count != 2 ||
				Math.Abs(through[0].CoordinateSystem.BasisZ.Normalize().DotProduct(mainAxis)) < 0.98) continue;
			foreach (Connector port in through)
			{
				if (SinglePeer(port, out Connector peer) && !visited.Contains(peer.Owner.Id.Value)) queue.Enqueue(peer.Owner);
			}
		}
		bool complete = !queue.Any(element => !visited.Contains(element.Id.Value));
		mayCreate = complete && !candidates.Any(candidate => DirectYGuards.SupportsBranchAngle(Math.Abs(candidate.BranchDot)));
		if (!DirectYGuards.TryChooseTarget(candidates.Select(candidate => new DirectYTargetCandidate(candidate.Distance,
			candidate.BranchDot)).ToArray(), complete, 20.0 / 304.8, out int index, out error)) return false;
		y = candidates[index].Y; return true;
	}

	internal static bool IsElbow(Element element, out FamilyInstance elbow)
	{
		elbow = element as FamilyInstance;
		return elbow?.MEPModel is MechanicalFitting mechanical && mechanical.PartType == PartType.Elbow &&
			elbow.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting && Ports(elbow).Count == 2;
	}
	internal static bool IsInline(Element element)
	{
		if (element is Pipe pipe) return IsStraightPipe(pipe);
		if (element is not FamilyInstance fitting || fitting.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeFitting ||
			IsElbow(fitting, out _)) return false;
		var ports = Ports(fitting);
		return ports.Count == 2 && ports.All(port => port.CoordinateSystem?.BasisZ != null) &&
			ports[0].CoordinateSystem.BasisZ.Normalize().DotProduct(ports[1].CoordinateSystem.BasisZ.Normalize()) < -0.98;
	}
	internal static bool IsY(Element element) => element is FamilyInstance fitting &&
		fitting.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting &&
		fitting.MEPModel is MechanicalFitting mechanical &&
		(mechanical.PartType == PartType.Wye || mechanical.PartType == PartType.Tee || mechanical.PartType == PartType.LateralTee);
	internal static bool SinglePeer(Connector connector, out Connector peer)
	{
		var refs = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector);
		peer = refs.Count == 1 ? refs[0] : null; return peer != null;
	}
	internal static IReadOnlyList<Connector> Ports(Element element) => FitSpaceConnectorService.GetPhysicalPipingConnectors(element);
	private static bool IsStraightPipe(Pipe pipe) => (pipe.Location as LocationCurve)?.Curve is Line;
	private static FitSpaceConnectorBinding Bind(Connector first, Connector second) => new FitSpaceConnectorBinding(
		FitSpaceConnectorService.CreateKey(first), FitSpaceConnectorService.CreateKey(second));
	private static bool TryMainPorts(IReadOnlyList<Connector> ports, out Connector[] mains, out Connector branch)
	{
		mains = null; branch = null;
		if (ports.Count != 3 || ports.Any(port => port.CoordinateSystem?.BasisZ == null)) return false;
		var pairs = new[] { (A:0,B:1), (A:0,B:2), (A:1,B:2) }
			.Select(pair => (pair.A, pair.B, Dot: ports[pair.A].CoordinateSystem.BasisZ.Normalize()
				.DotProduct(ports[pair.B].CoordinateSystem.BasisZ.Normalize())))
			.OrderBy(pair => pair.Dot).ToArray();
		if (pairs[0].Dot > -0.90 || pairs[1].Dot - pairs[0].Dot < 1e-4) return false;
		mains = new[] { ports[pairs[0].A], ports[pairs[0].B] };
		branch = ports[3 - pairs[0].A - pairs[0].B]; return true;
	}
}
