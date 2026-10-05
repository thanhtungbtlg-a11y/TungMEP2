using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn;

internal static class QRPipe2Runner
{
	private const double Epsilon = 1E-09;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Reference pipe1Reference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe 1");
		XYZ clickPoint = pipe1Reference.GlobalPoint;
		Reference pipe2Reference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe 2");
		Pipe pipe1 = document.GetElement(pipe1Reference) as Pipe;
		Pipe pipe2 = document.GetElement(pipe2Reference) as Pipe;
		if (pipe1 == null || pipe2 == null)
		{
			return;
		}
		using Transaction transaction = new Transaction(document, "QRPipe2");
		transaction.Start();
		CreateSlopePipe(document, pipe1, pipe2, clickPoint);
		transaction.Commit();
	}

	private static void CreateSlopePipe(Document document, Pipe pipe1, Pipe pipe2, XYZ clickPoint)
	{
		if (!TryGetPipeLine(pipe1, out Line pipe1Line) || !TryGetPipeLine(pipe2, out Line pipe2Line))
		{
			return;
		}
		Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, clickPoint);
		XYZ A = IntersectUnboundLineWithPlane(pipe1Line, plane);
		XYZ B = IntersectUnboundLineWithPlane(pipe2Line, plane);
		if (A == null || B == null)
		{
			return;
		}
		XYZ horizontalVector = new XYZ(B.X - A.X, B.Y - A.Y, 0.0);
		double horizontalRun = horizontalVector.GetLength();
		if (!(horizontalRun < 1E-09))
		{
			XYZ C = CreateSlope100EndPoint(A, B, horizontalRun);
			if (!(C.Z >= A.Z))
			{
				Line newPipeLine = Line.CreateBound(A, C);
				Pipe newPipe = PipeUtilities.CreatePipeFromTemplate(newPipeLine, pipe1);
				SetPipeSlopePercent(newPipe, 100.0);
				document.Regenerate();
			}
		}
	}

	private static XYZ CreateSlope100EndPoint(XYZ A, XYZ B, double horizontalRun)
	{
		return new XYZ(B.X, B.Y, A.Z - horizontalRun);
	}

	private static bool TryGetPipeLine(Pipe pipe, out Line line)
	{
		line = null;
		if (!(pipe.Location is LocationCurve locationCurve))
		{
			return false;
		}
		Line pipeLine = locationCurve.Curve as Line;
		if (pipeLine == null)
		{
			return false;
		}
		line = pipeLine;
		return true;
	}

	private static XYZ? IntersectUnboundLineWithPlane(Line line, Plane plane)
	{
		Line unbound = Line.CreateUnbound(line.GetEndPoint(0), line.Direction.Normalize());
		double denominator = plane.Normal.DotProduct(unbound.Direction);
		if (Math.Abs(denominator) < 1E-09)
		{
			return null;
		}
		double parameter = plane.Normal.DotProduct(plane.Origin - unbound.Origin) / denominator;
		return unbound.Origin + unbound.Direction.Multiply(parameter);
	}

	private static void SetPipeSlopePercent(Pipe pipe, double percent)
	{
		Parameter slopeParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
		if (slopeParameter != null && !slopeParameter.IsReadOnly)
		{
			slopeParameter.Set(percent / 100.0);
		}
	}
}
