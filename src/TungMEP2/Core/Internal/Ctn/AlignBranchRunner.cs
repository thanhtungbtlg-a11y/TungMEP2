using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn;

internal enum AlignBranchMode
{
	PreserveDirection,
	PerpendicularSameSlope
}

internal static class AlignBranchRunner
{
	private const double Epsilon = 1E-09;

	private sealed class StraightMepCurveSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return element is MEPCurve &&
			       element.Location is LocationCurve { Curve: Line };
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	internal static void Run(UIDocument uiDocument, Document document, AlignBranchMode mode)
	{
		StraightMepCurveSelectionFilter filter = new StraightMepCurveSelectionFilter();
		while (true)
		{
			Reference mainReference = uiDocument.Selection.PickObject(
				ObjectType.Element,
				filter,
				"Chọn ống/MEP curve chính.");
			Reference branchReference = uiDocument.Selection.PickObject(
				ObjectType.Element,
				filter,
				"Chọn ống/MEP curve nhánh cần căn.");

			MEPCurve main = document.GetElement(mainReference) as MEPCurve;
			MEPCurve branch = document.GetElement(branchReference) as MEPCurve;
			if (main == null || branch == null || main.Id == branch.Id)
			{
				TaskDialog.Show(GetDialogTitle(mode), "Phải chọn hai MEP curve thẳng và khác nhau.");
				continue;
			}

			if (!TryExecuteOnce(document, main, branch, mode, out string error))
			{
				TaskDialog.Show(GetDialogTitle(mode), error);
			}
		}
	}

	private static bool TryExecuteOnce(
		Document document,
		MEPCurve main,
		MEPCurve branch,
		AlignBranchMode mode,
		out string error)
	{
		error = string.Empty;
		if (branch.Pinned)
		{
			error = "Ống nhánh đang bị Pin.";
			return false;
		}

		if (!TryGetLine(main, out Line mainLine) || !TryGetLine(branch, out Line branchLine))
		{
			error = "Lệnh chỉ hỗ trợ MEP curve thẳng.";
			return false;
		}

		using Transaction transaction = new Transaction(document, GetTransactionName(mode));
		transaction.Start();
		try
		{
			XYZ targetBranchDirection = branchLine.Direction.Normalize();
			if (mode == AlignBranchMode.PerpendicularSameSlope)
			{
				if (!MepAlignmentGeometry.TryBuildPerpendicularSameSlopeDirection(
					    mainLine.Direction,
					    branchLine.Direction,
					    out targetBranchDirection))
				{
					error = "Không thể xác định phương vuông góc: ống chính có thể đang thẳng đứng.";
					transaction.RollBack();
					return false;
				}

				XYZ midpoint = (branchLine.GetEndPoint(0) + branchLine.GetEndPoint(1)) * 0.5;
				double halfLength = branchLine.Length * 0.5;
				Line rotatedBranch = Line.CreateBound(
					midpoint - targetBranchDirection.Multiply(halfLength),
					midpoint + targetBranchDirection.Multiply(halfLength));
				((LocationCurve)branch.Location).Curve = rotatedBranch;
				document.Regenerate();
				branchLine = rotatedBranch;
			}

			if (!MepAlignmentGeometry.TryComputeMoveToIntersect(
				    mainLine.GetEndPoint(0),
				    mainLine.Direction,
				    branchLine.GetEndPoint(0),
				    targetBranchDirection,
				    out XYZ moveVector,
				    out _))
			{
				error = "Hai tim ống song song hoặc gần song song nên không xác định được giao điểm.";
				transaction.RollBack();
				return false;
			}

			if (moveVector.GetLength() > Epsilon)
			{
				ElementTransformUtils.MoveElement(document, branch.Id, moveVector);
				document.Regenerate();
			}

			transaction.Commit();
			return true;
		}
		catch (Exception exception)
		{
			if (transaction.GetStatus() == TransactionStatus.Started)
			{
				transaction.RollBack();
			}

			error = "Không thể căn ống nhánh: " + exception.Message;
			return false;
		}
	}

	private static bool TryGetLine(MEPCurve curve, out Line line)
	{
		line = null;
		if (!(curve?.Location is LocationCurve { Curve: Line locationLine }))
		{
			return false;
		}

		line = locationLine;
		return line.Length > Epsilon;
	}

	private static string GetDialogTitle(AlignBranchMode mode)
	{
		return mode == AlignBranchMode.PreserveDirection
			? "Align Branch"
			: "Align Branch+";
	}

	private static string GetTransactionName(AlignBranchMode mode)
	{
		return "TungMEP2 - " + GetDialogTitle(mode);
	}
}
