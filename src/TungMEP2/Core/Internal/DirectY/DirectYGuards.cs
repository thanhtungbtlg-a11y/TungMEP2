using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal readonly struct DirectYTargetCandidate
{
	internal DirectYTargetCandidate(double distance, double branchMainDot)
	{ Distance = distance; BranchMainDot = branchMainDot; }
	internal double Distance { get; }
	internal double BranchMainDot { get; }
}

internal static class DirectYGuards
{
	internal static bool SupportsBranchAngle(double dot, bool perpendicularTee = false) =>
        Finite(dot) && (perpendicularTee
            ? Math.Abs(dot) <= Math.Sin(0.5 * Math.PI / 180)
            : Math.Abs(dot) >= 0.20 && Math.Abs(dot) <= 0.98);

	internal static bool TryChooseTarget(IReadOnlyList<DirectYTargetCandidate> candidates, bool scanComplete,
		double ambiguityDistance, out int index, out string error)
	{
		index = -1; error = string.Empty;
if (!scanComplete)
		{ error = "Tuyến chính vượt phạm vi quét 128 phần tử; chưa đủ dữ liệu để chọn Y an toàn."; return false; }
		if (candidates == null || !Finite(ambiguityDistance) || ambiguityDistance < 0)
		{ error = "Dữ liệu chọn Y không hợp lệ."; return false; }
		var eligible = Enumerable.Range(0, candidates.Count).Where(i =>
			Finite(candidates[i].Distance) && candidates[i].Distance >= 0 && SupportsBranchAngle(candidates[i].BranchMainDot))
			.OrderBy(i => candidates[i].Distance).ToArray();
		if (eligible.Length == 0)
		{ error = "Không tìm thấy Y nhánh nghiêng trên trục Pipe có cổng nhánh hở cùng DN. Tee nhánh 90° chưa được hỗ trợ."; return false; }
		if (eligible.Length > 1 && candidates[eligible[1]].Distance - candidates[eligible[0]].Distance < ambiguityDistance)
		{ error = "Có nhiều Y gần đầu nhánh tương đương nhau; không thể tự chọn Y an toàn."; return false; }
		index = eligible[0]; return true;
	}

	internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

// Distances and relative axis/offset angles are invariant under rigid rotation and translation.
internal readonly struct DirectYRigidSignature
{
	private DirectYRigidSignature(double distance, double axisDot, double firstOffsetDot, double secondOffsetDot)
	{ Distance = distance; AxisDot = axisDot; FirstOffsetDot = firstOffsetDot; SecondOffsetDot = secondOffsetDot; }
	private double Distance { get; }
	private double AxisDot { get; }
	private double FirstOffsetDot { get; }
	private double SecondOffsetDot { get; }

	internal static DirectYRigidSignature Create(Vector3d firstPoint, Vector3d secondPoint, Vector3d firstAxis, Vector3d secondAxis)
	{
		Vector3d offset = secondPoint - firstPoint;
		double length = offset.Length;
		if (!firstPoint.IsFinite || !secondPoint.IsFinite || !firstAxis.IsFinite || !secondAxis.IsFinite ||
			!DirectYGuards.Finite(length) || firstAxis.Length < 1e-10 || secondAxis.Length < 1e-10 ||
			!DirectYGuards.Finite(firstAxis.Length) || !DirectYGuards.Finite(secondAxis.Length))
			return new DirectYRigidSignature(double.NaN, double.NaN, double.NaN, double.NaN);
		Vector3d a = firstAxis * (1 / firstAxis.Length), b = secondAxis * (1 / secondAxis.Length);
		Vector3d direction = length < 1e-10 ? default : offset * (1 / length);
		return new DirectYRigidSignature(length, a.Dot(b), a.Dot(direction), b.Dot(direction));
	}

	internal bool IsPreservedBy(DirectYRigidSignature current, double lengthTolerance, double angularTolerance)
	{
		return DirectYGuards.Finite(Distance) && DirectYGuards.Finite(current.Distance) &&
			DirectYGuards.Finite(lengthTolerance) && lengthTolerance > 0 &&
			DirectYGuards.Finite(angularTolerance) && angularTolerance > 0 &&
			Math.Abs(Distance - current.Distance) <= lengthTolerance &&
			AngleUnchanged(AxisDot, current.AxisDot, angularTolerance) &&
			AngleUnchanged(FirstOffsetDot, current.FirstOffsetDot, angularTolerance) &&
			AngleUnchanged(SecondOffsetDot, current.SecondOffsetDot, angularTolerance);
	}

	private static bool AngleUnchanged(double original, double current, double tolerance) =>
		DirectYGuards.Finite(original) && DirectYGuards.Finite(current) &&
Math.Abs(Math.Acos(Math.Max(-1, Math.Min(1, original))) -
			Math.Acos(Math.Max(-1, Math.Min(1, current)))) <= tolerance;
}
