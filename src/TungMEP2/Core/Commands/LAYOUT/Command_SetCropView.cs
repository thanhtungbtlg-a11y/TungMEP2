using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_SetCropView : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<Element> selected = (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id) into element
				where element != null
				select element).Cast<Element>().ToList();
			if (selected.Count == 0)
			{
				TaskDialog.Show("SetCropView", "Hãy chọn một viewport và biên dạng detail lines khép kín trước khi chạy lệnh.");
				return;
			}
			Viewport viewport = selected.OfType<Viewport>().FirstOrDefault();
			if (viewport == null)
			{
				TaskDialog.Show("SetCropView", "Cần chọn ít nhất một viewport.");
				return;
			}
			List<CurveElement> boundaryCurves = selected.OfType<CurveElement>().ToList();
			if (boundaryCurves.Count < 3)
			{
				TaskDialog.Show("SetCropView", "Cần chọn một polygon khép kín (tối thiểu 3 detail lines).");
			}
			else
			{
				CropRegionFromSheetBoundary.Apply(base.Document, viewport, boundaryCurves);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("SetCropView", ex2.ToString());
		}
	}
}
