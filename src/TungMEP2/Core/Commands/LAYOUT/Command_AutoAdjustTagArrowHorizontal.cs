using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_AutoAdjustTagArrowHorizontal : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
			if (selectedIds.Count == 0)
			{
				TaskDialog.Show("AutoAdjustTagArrow-H", "Hãy chọn tag trước khi chạy lệnh.");
				return;
			}
			int count = 0;
			using (Transaction transaction = new Transaction(base.Document, "Auto Adjust Tag Arrow Horizontal"))
			{
				transaction.Start();
				foreach (ElementId elementId in selectedIds)
				{
					Element element = base.Document.GetElement(elementId);
					if (element is IndependentTag || element is SpatialElementTag)
					{
						TagUtilities.AutoAdjustTagArrow(element, LeaderElbowMode.HorizontalFirst);
						count++;
					}
				}
				transaction.Commit();
			}
			if (count == 0)
			{
				TaskDialog.Show("AutoAdjustTagArrow-H", "Danh sách chọn không có tag hợp lệ.");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("AutoAdjustTagArrow-H", ex2.ToString());
		}
	}
}
