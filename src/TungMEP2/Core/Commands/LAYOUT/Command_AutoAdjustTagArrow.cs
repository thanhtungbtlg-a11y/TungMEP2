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
public class Command_AutoAdjustTagArrow : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
			if (selectedIds.Count == 0)
			{
				TaskDialog.Show("AutoAdjustTagArrow", "Hãy chọn tag trước khi chạy lệnh.");
				return;
			}
			int count = 0;
			using (Transaction tx = new Transaction(base.Document, "Auto Adjust Tag Arrow"))
			{
				tx.Start();
				foreach (ElementId id in selectedIds)
				{
					Element element = base.Document.GetElement(id);
					if (element is IndependentTag || element is SpatialElementTag)
					{
						TagUtilities.AutoAdjustTagArrow(element);
						count++;
					}
				}
				tx.Commit();
			}
			if (count == 0)
			{
				TaskDialog.Show("AutoAdjustTagArrow", "Danh sách chọn không có tag hợp lệ.");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("AutoAdjustTagArrow", ex2.ToString());
		}
	}
}
