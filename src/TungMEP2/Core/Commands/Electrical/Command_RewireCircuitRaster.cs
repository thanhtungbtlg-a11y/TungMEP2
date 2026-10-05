using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Electrical;

namespace VP_RevitAPI.Commands.Electrical;

[Transaction(TransactionMode.Manual)]
public class Command_RewireCircuitRaster : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			RewireCircuitRasterRunner.Run(base.UiDocument, base.Document);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Wire Raster", ex2.ToString());
		}
	}
}
