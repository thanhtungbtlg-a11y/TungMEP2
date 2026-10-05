using System;
using System.Collections.Generic;
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
public class Command_Gom2E1Yv1 : SecuredExternalCommand
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
			Reference mainRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Chọn ống chính");
			Reference subRef = base.UiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Chọn ống nhánh");
			Pipe mainPipe = (Pipe)base.Document.GetElement(mainRef);
			Pipe subPipe = (Pipe)base.Document.GetElement(subRef);
			List<Line> lines = PipeUtilities.GOM2E1Yv1(mainPipe, subPipe);
			if (lines == null || lines.Count < 3)
			{
				TaskDialog.Show("Gom 2E1Y", "Không tìm được đường 2E1Y.");
				return;
			}
			Line line24 = lines[1];
			Line line43 = lines[2];
			using TransactionGroup group = new TransactionGroup(base.Document, "Gom 2E1Y");
			group.Start();
			using (Transaction tx = new Transaction(base.Document, "Create and connect 2E1Y pipes"))
			{
				tx.Start();
				Pipe pipe1 = PipeUtilities.CreatePipeFromTemplate(line24, subPipe);
				Pipe pipe2 = PipeUtilities.CreatePipeFromTemplate(line43, subPipe);
				TryConnect(delegate
				{
					subPipe.CreateElbow(pipe1);
				});
				TryConnect(delegate
				{
					pipe1.CreateElbow(pipe2);
				});
				TryConnect(delegate
				{
					PipeUtilities.TeeMaster(mainPipe, pipe2);
				});
				tx.Commit();
			}
			group.Assimilate();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Gom 2E1Y", ex2.ToString());
		}
	}

	private static bool TryConnect(Action action)
	{
		try
		{
			action();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
