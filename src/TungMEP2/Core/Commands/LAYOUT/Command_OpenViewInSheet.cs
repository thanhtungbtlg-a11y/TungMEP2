using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_OpenViewInSheet : SecuredExternalCommand
{
	private sealed class ViewportSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return element is Viewport;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private const string DialogTitle = "Mở View Trong Sheet";

	protected override void ExecuteCore()
	{
		try
		{
			if (!(base.UiDocument.ActiveView is ViewSheet))
			{
				TaskDialog.Show("Mở View Trong Sheet", "Vui lòng mở ViewSheet trước, sau đó chạy lại lệnh.");
				return;
			}
			Reference viewportReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new ViewportSelectionFilter(), "Chọn 1 viewport trong ViewSheet.");
			if (!(base.Document.GetElement(viewportReference) is Viewport viewport))
			{
				TaskDialog.Show("Mở View Trong Sheet", "Phần tử được chọn không phải viewport hợp lệ.");
			}
			else if (!(base.Document.GetElement(viewport.ViewId) is View targetView))
			{
				TaskDialog.Show("Mở View Trong Sheet", "Không tìm thấy view từ viewport đã chọn.");
			}
			else
			{
				base.UiDocument.RequestViewChange(targetView);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Mở View Trong Sheet", ex2.ToString());
		}
	}
}
