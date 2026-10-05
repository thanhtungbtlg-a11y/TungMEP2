using System;
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
public class Command_Branch : SecuredExternalCommand
{
	private sealed class PipeSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is Pipe;
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
			Reference mainRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Chọn pipeMain");
			Reference subRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Chọn pipeSub");
			Pipe mainPipe = (Pipe)base.Document.GetElement(mainRef);
			Pipe subPipe = (Pipe)base.Document.GetElement(subRef);
			using TransactionGroup group = new TransactionGroup(base.Document, "Branch");
			group.Start();
			using (Transaction txAlign = new Transaction(base.Document, "Align pipes for Branch"))
			{
				txAlign.Start();
				Line subLineRaw = (Line)((LocationCurve)subPipe.Location).Curve;
				if (IsVerticalOrSteep(subLineRaw))
				{
					XYZ moveVector = GeometryUtilities.Align3D(subPipe, mainPipe);
					if (moveVector.GetLength() > 1E-09)
					{
						ElementTransformUtils.MoveElement(base.Document, mainPipe.Id, moveVector);
					}
				}
				else
				{
					XYZ moveVector = GeometryUtilities.Align3D(mainPipe, subPipe);
					if (moveVector.GetLength() > 1E-09)
					{
						ElementTransformUtils.MoveElement(base.Document, subPipe.Id, moveVector);
					}
				}
				base.Document.Regenerate();
				txAlign.Commit();
			}
			Line lineMain = NormalizeLineByZ((Line)((LocationCurve)mainPipe.Location).Curve);
			Line lineSub = NormalizeLineByZ((Line)((LocationCurve)subPipe.Location).Curve);
			XYZ point1 = TryIntersectUnbound(lineMain, lineSub);
			if (point1 == null)
			{
				TaskDialog.Show("Branch", "Không tìm được giao điểm unbound của 2 pipe.");
				group.RollBack();
				return;
			}
			double l1 = PipeUtilities.GetElbowLengthInternal(subPipe);
			double l2 = l1 * 1.05;
			double d = l2 * Math.Sqrt(2.0);
			XYZ mainDirection = lineMain.Direction.Normalize();
			XYZ subDirection = lineSub.Direction.Normalize();
			XYZ point2 = point1 + mainDirection.Multiply(d);
			XYZ point3 = point1 - subDirection.Multiply(d);
			Pipe pipe1;
			using (Transaction txCreate = new Transaction(base.Document, "Create Branch pipe"))
			{
				txCreate.Start();
				pipe1 = PipeUtilities.CreatePipeFromTemplate(Line.CreateBound(point3, point2), subPipe);
				txCreate.Commit();
			}
			TryConnect("Create elbow (pipeSub-pipe1)", delegate
			{
				subPipe.CreateElbow(pipe1);
			});
			TryConnect("Create elbow (pipe1-pipeMain)", delegate
			{
				pipe1.CreateElbow(mainPipe);
			});
			group.Assimilate();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Branch", ex2.ToString());
		}
	}

	private bool TryConnect(string transactionName, Action action)
	{
		using Transaction tx = new Transaction(base.Document, transactionName);
		try
		{
			tx.Start();
			action();
			tx.Commit();
			return true;
		}
		catch
		{
			if (tx.GetStatus() == TransactionStatus.Started)
			{
				tx.RollBack();
			}
			return false;
		}
	}

	private static bool IsVerticalOrSteep(Line line)
	{
		XYZ direction = line.Direction.Normalize();
		double run = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
		double rise = Math.Abs(direction.Z);
		bool isVertical = run < 1E-09;
		bool isSteep = !isVertical && rise / run > 0.5;
		return isVertical || isSteep;
	}

	private static XYZ? TryIntersectUnbound(Line line1, Line line2)
	{
		XYZ d1 = line1.Direction.Normalize();
		XYZ d2 = line2.Direction.Normalize();
		if (d1.CrossProduct(d2).GetLength() < 1E-09)
		{
			return null;
		}
		Line unbound1 = Line.CreateUnbound(line1.GetEndPoint(0), d1);
		Line unbound2 = Line.CreateUnbound(line2.GetEndPoint(0), d2);
		IntersectionResultArray intersections;
		SetComparisonResult result = unbound1.Intersect(unbound2, out intersections);
		if (result != SetComparisonResult.Overlap || intersections == null || intersections.Size == 0)
		{
			return null;
		}
		return intersections.get_Item(0).XYZPoint;
	}

	private static Line NormalizeLineByZ(Line line)
	{
		XYZ start = line.GetEndPoint(0);
		XYZ end = line.GetEndPoint(1);
		if (start.Z < end.Z)
		{
			return Line.CreateBound(end, start);
		}
		return line;
	}
}
