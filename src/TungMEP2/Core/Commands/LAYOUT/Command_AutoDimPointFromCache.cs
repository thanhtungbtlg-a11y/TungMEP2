using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_AutoDimPointFromCache : SecuredExternalCommand
{
	private const string DialogTitle = "AutoDIM From Cache";

	protected override void ExecuteCore()
	{
		try
		{
			List<FamilyInstance> targets = base.UiDocument.Selection.GetElementIds().Select(base.Document.GetElement).OfType<FamilyInstance>()
				.ToList();
			if (targets.Count == 0)
			{
				TaskDialog.Show("AutoDIM From Cache", "Vui lòng chọn FamilyInstance point trước khi chạy AutoDIM.");
				return;
			}
			AutoDimDatabaseExcel databaseExcel = new AutoDimDatabaseExcel();
			if (!databaseExcel.TryRead(base.Document, out AutoDimDatabaseSnapshot snapshot, out string error))
			{
				TaskDialog.Show("AutoDIM From Cache", error);
				return;
			}
			AutoDimFamilyInstancePointWallService service = new AutoDimFamilyInstancePointWallService(base.Document, snapshot.DistanceMillimeters);
			service.Execute(targets, snapshot);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("AutoDIM From Cache", ex2.ToString());
		}
	}
}
