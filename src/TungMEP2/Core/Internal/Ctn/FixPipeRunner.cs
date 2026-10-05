using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn;

internal static class FixPipeRunner
{
	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<Pipe> pipes = GetTargetPipes(uiDocument, document);
		if (pipes.Count == 0)
		{
			return;
		}
		using Transaction transaction = new Transaction(document, "FixPipe");
		transaction.Start();
		foreach (Pipe pipe in pipes)
		{
			try
			{
				FixPipe(pipe);
			}
			catch
			{
			}
		}
		transaction.Commit();
	}

	private static List<Pipe> GetTargetPipes(UIDocument uiDocument, Document document)
	{
		List<Pipe> selected = uiDocument.Selection.GetElementIds().Select(document.GetElement).OfType<Pipe>()
			.ToList();
		if (selected.Count > 0)
		{
			return selected;
		}
		IList<Reference> picked = uiDocument.Selection.PickObjects(ObjectType.Element, new Isf.Pipe(), "Chọn Pipe cần Fix");
		return picked.Select((Reference reference) => document.GetElement(reference)).OfType<Pipe>().ToList();
	}

	private static void FixPipe(Pipe pipe)
	{
		Line line = pipe.GetLocation();
		XYZ end0 = line.GetEndPoint(0);
		XYZ end1 = line.GetEndPoint(1);
		end1 = new XYZ(end0.X, end0.Y, end1.Z);
		DisconnectPipeToPipeConnections(pipe);
		SetPipeLocation(pipe, end0, end1);
	}

	private static void SetPipeLocation(Pipe pipe, XYZ newStart, XYZ newEnd)
	{
		if (pipe.Location is LocationCurve locationCurve)
		{
			Line newLine = Line.CreateBound(newStart, newEnd);
			locationCurve.Curve = newLine;
		}
	}

	private static void DisconnectPipeToPipeConnections(Pipe pipe)
	{
		List<Connector> endConnectors = (from Connector connector2 in pipe.ConnectorManager.Connectors
			where connector2.ConnectorType == ConnectorType.End
			select connector2).ToList();
		foreach (Connector connector in endConnectors)
		{
			List<Connector> references = connector.AllRefs?.Cast<Connector>().ToList() ?? new List<Connector>();
			foreach (Connector reference in references)
			{
				if (reference.Owner.Id == pipe.Id || !(reference.Owner is Pipe))
				{
					continue;
				}
				try
				{
					if (connector.IsConnectedTo(reference))
					{
						connector.DisconnectFrom(reference);
					}
				}
				catch
				{
				}
			}
		}
	}
}
