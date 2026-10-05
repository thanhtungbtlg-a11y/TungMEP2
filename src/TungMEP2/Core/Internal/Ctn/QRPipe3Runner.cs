using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn;

internal static class QRPipe3Runner
{
	private const double Epsilon = 1E-09;

	private const double VerticalTolerance = 0.01;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Reference targetReference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe Target");
		Reference pipe1Reference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe 1 (thẳng đứng)");
		Reference pipe2Reference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe 2 (thẳng đứng)");
		Pipe targetPipe = document.GetElement(targetReference) as Pipe;
		Pipe pipe1 = document.GetElement(pipe1Reference) as Pipe;
		Pipe pipe2 = document.GetElement(pipe2Reference) as Pipe;
		if (targetPipe == null || pipe1 == null || pipe2 == null)
		{
			return;
		}
		using Transaction transaction = new Transaction(document, "QRPipe3");
		transaction.Start();
		AlignTargetPipe(document, targetPipe, pipe1, pipe2);
		transaction.Commit();
	}

	private static void AlignTargetPipe(Document document, Pipe targetPipe, Pipe pipe1, Pipe pipe2)
	{
		if (!(targetPipe.Location is LocationCurve { Curve: Line targetLine } targetLocation) || !TryGetPipeLine(pipe1, out Line pipe1Line) || !TryGetPipeLine(pipe2, out Line pipe2Line) || !IsVertical(pipe1Line) || !IsVertical(pipe2Line))
		{
			return;
		}
		XYZ point0 = targetLine.GetEndPoint(0);
		XYZ point1 = targetLine.GetEndPoint(1);
		XYZ topPoint = ((point0.Z >= point1.Z) ? point0 : point1);
		XYZ bottomPoint = ((point0.Z >= point1.Z) ? point1 : point0);
		Plane topPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, topPoint);
		Plane bottomPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, bottomPoint);
		XYZ n1 = IntersectLineWithPlane(pipe1Line, topPlane);
		XYZ n2 = IntersectLineWithPlane(pipe2Line, bottomPlane);
		if (n1 != null && n2 != null && !(n1.DistanceTo(n2) < 1E-09))
		{
			DisconnectPipeEndConnectors(targetPipe);
			Line slopeLine = CreateSlope100Line(n1, n2);
			if (!(slopeLine == null))
			{
				targetLocation.Curve = slopeLine;
				SetPipeSlopePercent(targetPipe, 100.0);
				document.Regenerate();
			}
		}
	}

	private static Line? CreateSlope100Line(XYZ firstPoint, XYZ secondPoint)
	{
		XYZ lowPoint = ((firstPoint.Z <= secondPoint.Z) ? firstPoint : secondPoint);
		XYZ highPoint = ((firstPoint.Z <= secondPoint.Z) ? secondPoint : firstPoint);
		XYZ horizontalVector = new XYZ(highPoint.X - lowPoint.X, highPoint.Y - lowPoint.Y, 0.0);
		double horizontalRun = horizontalVector.GetLength();
		if (horizontalRun < 1E-09)
		{
			return null;
		}
		XYZ horizontalDirection = horizontalVector.Normalize();
		XYZ adjustedHighPoint = new XYZ(lowPoint.X + horizontalDirection.X * horizontalRun, lowPoint.Y + horizontalDirection.Y * horizontalRun, lowPoint.Z + horizontalRun);
		return Line.CreateBound(lowPoint, adjustedHighPoint);
	}

	private static void SetPipeSlopePercent(Pipe pipe, double percent)
	{
		try
		{
			Parameter slopeParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
			if (slopeParameter != null && !slopeParameter.IsReadOnly)
			{
				slopeParameter.Set(percent / 100.0);
			}
		}
		catch
		{
		}
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

	private static bool IsVertical(Line line)
	{
		XYZ direction = line.Direction.Normalize();
		double horizontal = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
		return horizontal <= 0.01;
	}

	private static XYZ? IntersectLineWithPlane(Line line, Plane plane)
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

	private static void DisconnectPipeEndConnectors(Pipe pipe)
	{
		List<Connector> pipeEndConnectors = (from Connector connector in pipe.ConnectorManager.Connectors
			where connector.ConnectorType == ConnectorType.End
			select connector).ToList();
		foreach (Connector pipeEnd in pipeEndConnectors)
		{
			List<Connector> references = pipeEnd.AllRefs?.Cast<Connector>().ToList() ?? new List<Connector>();
			foreach (Connector referenceConnector in references)
			{
				if (referenceConnector.Owner.Id == pipe.Id)
				{
					continue;
				}
				try
				{
					if (pipeEnd.IsConnectedTo(referenceConnector))
					{
						pipeEnd.DisconnectFrom(referenceConnector);
					}
				}
				catch
				{
				}
			}
		}
	}
}
