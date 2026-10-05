using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_Gom1E1Y : SecuredExternalCommand
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
			Line gomLine = PipeUtilities.GOM1E1Y(base.Document, mainPipe, subPipe);
			if (gomLine == null)
			{
				return;
			}
			using TransactionGroup group = new TransactionGroup(base.Document, "Gom 1E1Y");
			group.Start();
			Pipe newPipe;
			using (Transaction tx = new Transaction(base.Document, "Create 1E1Y Pipe"))
			{
				tx.Start();
				newPipe = PipeUtilities.CreatePipeFromTemplate(gomLine, subPipe);
				subPipe.CreateElbow(newPipe);
				tx.Commit();
			}
			try
			{
				using Transaction tx2 = new Transaction(base.Document, "Tee Master");
				tx2.Start();
				PipeUtilities.TeeMaster(mainPipe, newPipe);
				tx2.Commit();
			}
			catch (Exception)
			{
			}
			group.Assimilate();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception)
		{
		}
	}
}
