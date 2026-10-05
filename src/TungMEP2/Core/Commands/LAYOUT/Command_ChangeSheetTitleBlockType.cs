using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Layout.TitleBlocks;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_ChangeSheetTitleBlockType : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ChangeSheetTitleBlockTypeRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (System.OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			TaskDialog.Show("ChangeSheetTitleBlockType", ex3.Message);
		}
	}
}
