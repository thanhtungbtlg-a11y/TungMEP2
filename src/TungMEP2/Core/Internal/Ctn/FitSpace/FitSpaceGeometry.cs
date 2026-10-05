using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Linq;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceGeometry
{
	private const double MillimetresPerFoot = 304.8;

	internal static bool TryBuildTarget(
		Document document,
		FitSpaceCluster cluster,
		out FitSpaceTargetGeometry target,
		out string error)
	{
		target = null;
		error = string.Empty;
		if (!(document.GetElement(cluster.OuterPipe1Id) is Pipe outer1) ||
			!(document.GetElement(cluster.MiddlePipeId) is Pipe middle) ||
			!(document.GetElement(cluster.OuterPipe2Id) is Pipe outer2) ||
			!TryGetLine(outer1, out Line outerLine1) ||
			!TryGetLine(middle, out Line middleLine) ||
			!TryGetLine(outer2, out Line outerLine2))
		{
			error = "Ba Pipe của cụm phải còn tồn tại và là đoạn thẳng.";
			return false;
		}

		FitSpaceConnectorBinding outerBinding1 = cluster.LocalBindings[0];
		FitSpaceConnectorBinding innerBinding1 = cluster.LocalBindings[1];
		FitSpaceConnectorBinding innerBinding2 = cluster.LocalBindings[2];
		FitSpaceConnectorBinding outerBinding2 = cluster.LocalBindings[3];
		Connector elbow1Outer = FindConnectorOnOwner(document, outerBinding1, cluster.Elbow1Id);
		Connector elbow1Inner = FindConnectorOnOwner(document, innerBinding1, cluster.Elbow1Id);
		Connector elbow2Inner = FindConnectorOnOwner(document, innerBinding2, cluster.Elbow2Id);
		Connector elbow2Outer = FindConnectorOnOwner(document, outerBinding2, cluster.Elbow2Id);
		if (elbow1Outer == null || elbow1Inner == null || elbow2Inner == null || elbow2Outer == null)
		{
			error = "Không tìm lại được connector elbow theo ID ban đầu.";
			return false;
		}

		double intersectionTolerance = Math.Max(document.Application.ShortCurveTolerance, 0.1 / MillimetresPerFoot);
		if (!TryFindElbowApex(elbow1Outer, elbow1Inner, intersectionTolerance, out XYZ currentApex1) ||
			!TryFindElbowApex(elbow2Inner, elbow2Outer, intersectionTolerance, out XYZ currentApex2))
		{
			error = "Không xác định được giao điểm trục của một elbow.";
			return false;
		}

		Line unboundOuter1 = Line.CreateUnbound(outerLine1.Origin, outerLine1.Direction);
		Line unboundOuter2 = Line.CreateUnbound(outerLine2.Origin, outerLine2.Direction);
		if (!TryIntersectUnboundLines(unboundOuter1, unboundOuter2, intersectionTolerance, out XYZ virtualIntersection))
		{
			error = "Hai trục Pipe ngoài không đồng phẳng hoặc không có giao điểm ổn định.";
			return false;
		}

		if (!TryNormalize(cluster.FarEnd1.Point - virtualIntersection, out XYZ ray1) ||
			!TryNormalize(cluster.FarEnd2.Point - virtualIntersection, out XYZ ray2))
		{
			error = "Đầu xa Pipe ngoài trùng giao điểm ảo.";
			return false;
		}

		double includedAngle = ray1.AngleTo(ray2);
		double referenceSize;
		try
		{
			referenceSize = new[] { outer1, middle, outer2 }
				.Select(PipeUtilities.GetElbowLengthInternal)
				.Max();
		}
		catch (Exception exception)
		{
			error = "Không đọc được chiều dài elbow từ ElbowData: " + exception.Message;
			return false;
		}
		if (!FitSpaceMath.TryCalculate(
			new FitSpaceScalarInput(referenceSize, includedAngle),
			out FitSpaceScalarTarget scalarTarget,
			out error))
		{
			return false;
		}

		XYZ targetApex1 = virtualIntersection + ray1 * scalarTarget.Standoff;
		XYZ targetApex2 = virtualIntersection + ray2 * scalarTarget.Standoff;
		XYZ translation1 = targetApex1 - currentApex1;
		XYZ translation2 = targetApex2 - currentApex2;
		double directionTolerance = 1e-6;
		if (!IsParallelOrZero(translation1, ray1, directionTolerance) ||
			!IsParallelOrZero(translation2, ray2, directionTolerance))
		{
			error = "Vị trí hiện tại cần xoay elbow hoặc tạo offset; FitSpace chỉ tịnh tiến elbow.";
			return false;
		}

		XYZ targetOuter1Point = elbow1Outer.Origin + translation1;
		XYZ targetInner1Point = elbow1Inner.Origin + translation1;
		XYZ targetInner2Point = elbow2Inner.Origin + translation2;
		XYZ targetOuter2Point = elbow2Outer.Origin + translation2;
		double minimumLength = Math.Max(document.Application.ShortCurveTolerance * 2.0, 5.0 / MillimetresPerFoot);
		double farProjection1 = (cluster.FarEnd1.Point - virtualIntersection).DotProduct(ray1);
		double farProjection2 = (cluster.FarEnd2.Point - virtualIntersection).DotProduct(ray2);
		double targetProjection1 = (targetOuter1Point - virtualIntersection).DotProduct(ray1);
		double targetProjection2 = (targetOuter2Point - virtualIntersection).DotProduct(ray2);
		if (farProjection1 - targetProjection1 < minimumLength || farProjection2 - targetProjection2 < minimumLength)
		{
			error = "Khoảng Pipe ngoài không đủ để đưa elbow tới vị trí chuẩn mà vẫn giữ đầu xa cố định.";
			return false;
		}
		if (!TryCreateBoundLine(cluster.FarEnd1.Point, targetOuter1Point, minimumLength, out Line targetOuterLine1) ||
			!TryCreateBoundLine(targetInner1Point, targetInner2Point, minimumLength, out Line targetMiddleLine) ||
			!TryCreateBoundLine(targetOuter2Point, cluster.FarEnd2.Point, minimumLength, out Line targetOuterLine2))
		{
			error = "Sau khi chỉnh, một đoạn Pipe sẽ ngắn hơn giới hạn an toàn.";
			return false;
		}
		FitSpaceConnectorKey outer1LocalKey = ConnectorKeyOnOwner(outerBinding1, cluster.OuterPipe1Id);
		FitSpaceConnectorKey middle1Key = ConnectorKeyOnOwner(innerBinding1, cluster.MiddlePipeId);
		FitSpaceConnectorKey middle2Key = ConnectorKeyOnOwner(innerBinding2, cluster.MiddlePipeId);
		FitSpaceConnectorKey outer2LocalKey = ConnectorKeyOnOwner(outerBinding2, cluster.OuterPipe2Id);
		if (outer1LocalKey == null || middle1Key == null || middle2Key == null || outer2LocalKey == null)
		{
			error = "Không xác định được thứ tự endpoint Pipe ban đầu.";
			return false;
		}
		targetOuterLine1 = CreateLinePreservingEndpointOrder(
			outerLine1,
			cluster.FarEnd1.Point,
			cluster.FarEnd1.Point,
			outer1LocalKey.OriginalOrigin,
			targetOuter1Point);
		targetMiddleLine = CreateLinePreservingEndpointOrder(
			middleLine,
			middle1Key.OriginalOrigin,
			targetInner1Point,
			middle2Key.OriginalOrigin,
			targetInner2Point);
		targetOuterLine2 = CreateLinePreservingEndpointOrder(
			outerLine2,
			outer2LocalKey.OriginalOrigin,
			targetOuter2Point,
			cluster.FarEnd2.Point,
			cluster.FarEnd2.Point);

		if (!AreParallel(targetMiddleLine.Direction, middleLine.Direction, directionTolerance) ||
			!AreParallel(targetOuterLine1.Direction, outerLine1.Direction, directionTolerance) ||
			!AreParallel(targetOuterLine2.Direction, outerLine2.Direction, directionTolerance))
		{
			error = "Hình học mục tiêu làm đổi hướng Pipe; cụm này cần xử lý thủ công.";
			return false;
		}

		double normalizeTolerance = 0.5 / MillimetresPerFoot;
		double currentStandoff1 = currentApex1.DistanceTo(virtualIntersection);
		double currentStandoff2 = currentApex2.DistanceTo(virtualIntersection);
		bool isAlreadyNormalized = IsAlreadyNormalized(
			currentStandoff1,
			currentStandoff2,
			scalarTarget.Standoff,
			normalizeTolerance);
		target = new FitSpaceTargetGeometry(
			currentApex1,
			currentApex2,
			targetApex1,
			targetApex2,
			translation1,
			translation2,
			targetOuterLine1,
			targetMiddleLine,
			targetOuterLine2,
			referenceSize,
			includedAngle,
			isAlreadyNormalized);
		return true;
	}

	internal static bool TryFindElbowApex(
		Connector first,
		Connector second,
		double tolerance,
		out XYZ apex)
	{
		apex = null;
		if (first?.CoordinateSystem == null || second?.CoordinateSystem == null)
		{
			return false;
		}

		XYZ firstAxis = first.CoordinateSystem.BasisZ;
		XYZ secondAxis = second.CoordinateSystem.BasisZ;
		if (!TryNormalize(firstAxis, out firstAxis) || !TryNormalize(secondAxis, out secondAxis))
		{
			return false;
		}

		Line firstLine = Line.CreateUnbound(first.Origin, firstAxis);
		Line secondLine = Line.CreateUnbound(second.Origin, secondAxis);
		return TryIntersectUnboundLines(firstLine, secondLine, tolerance, out apex);
	}

	private static bool TryIntersectUnboundLines(Line first, Line second, double tolerance, out XYZ intersection)
	{
		intersection = null;
		if (first == null || second == null || tolerance < 0)
		{
			return false;
		}

		XYZ p1 = first.Origin;
		XYZ p2 = second.Origin;
		XYZ d1 = first.Direction.Normalize();
		XYZ d2 = second.Direction.Normalize();
		XYZ between = p1 - p2;
		double a = d1.DotProduct(d1);
		double b = d1.DotProduct(d2);
		double c = d2.DotProduct(d2);
		double d = d1.DotProduct(between);
		double e = d2.DotProduct(between);
		double denominator = a * c - b * b;
		if (Math.Abs(denominator) <= 1e-12)
		{
			return false;
		}

		double firstParameter = (b * e - c * d) / denominator;
		double secondParameter = (a * e - b * d) / denominator;
		XYZ closest1 = p1 + d1 * firstParameter;
		XYZ closest2 = p2 + d2 * secondParameter;
		if (closest1.DistanceTo(closest2) > tolerance)
		{
			return false;
		}

		intersection = (closest1 + closest2) * 0.5;
		return true;
	}

	private static Connector FindConnectorOnOwner(
		Document document,
		FitSpaceConnectorBinding binding,
		ElementId ownerId)
	{
		FitSpaceConnectorKey key = binding.First.OwnerId == ownerId ? binding.First : binding.Second;
		return key.OwnerId == ownerId ? FitSpaceConnectorService.FindByKey(document, key) : null;
	}

	private static FitSpaceConnectorKey ConnectorKeyOnOwner(
		FitSpaceConnectorBinding binding,
		ElementId ownerId)
	{
		if (binding.First.OwnerId == ownerId)
		{
			return binding.First;
		}
		return binding.Second.OwnerId == ownerId ? binding.Second : null;
	}

	private static Line CreateLinePreservingEndpointOrder(
		Line original,
		XYZ originalPointA,
		XYZ targetPointA,
		XYZ originalPointB,
		XYZ targetPointB)
	{
		double direct = original.GetEndPoint(0).DistanceTo(originalPointA) +
			original.GetEndPoint(1).DistanceTo(originalPointB);
		double reversed = original.GetEndPoint(0).DistanceTo(originalPointB) +
			original.GetEndPoint(1).DistanceTo(originalPointA);
		return direct <= reversed
			? Line.CreateBound(targetPointA, targetPointB)
			: Line.CreateBound(targetPointB, targetPointA);
	}

	private static bool TryCreateBoundLine(XYZ start, XYZ end, double minimumLength, out Line line)
	{
		line = null;
		if (start == null || end == null || start.DistanceTo(end) < minimumLength)
		{
			return false;
		}
		line = Line.CreateBound(start, end);
		return true;
	}

	private static bool TryGetLine(Pipe pipe, out Line line)
	{
		line = (pipe.Location as LocationCurve)?.Curve as Line;
		return line != null && line.IsBound;
	}

	private static bool TryNormalize(XYZ vector, out XYZ normalized)
	{
		normalized = null;
		if (vector == null || vector.GetLength() <= 1e-12)
		{
			return false;
		}
		normalized = vector.Normalize();
		return true;
	}

	private static bool IsParallelOrZero(XYZ vector, XYZ direction, double tolerance)
	{
		return vector.GetLength() <= tolerance || vector.CrossProduct(direction).GetLength() <= tolerance * vector.GetLength();
	}

	private static bool AreParallel(XYZ first, XYZ second, double tolerance)
	{
		return first.CrossProduct(second).GetLength() <= tolerance;
	}

	private static bool IsAlreadyNormalized(
		double currentStandoff1,
		double currentStandoff2,
		double targetStandoff,
		double tolerance)
	{
		return FitSpaceMath.IsNormalized(currentStandoff1, targetStandoff, tolerance) &&
			FitSpaceMath.IsNormalized(currentStandoff2, targetStandoff, tolerance);
	}
}
