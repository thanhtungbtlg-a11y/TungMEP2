using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Electrical;

internal static class ObjectDetectionRunner
{
	private const string DialogTitle = "Object Detection";

	private const string ParameterArea = "E_Area";

	private const string ParameterWire = "E_Wire";

	private const string ParameterLoadName = "E_LoadName";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<Element> candidates = new FilteredElementCollector(document, uiDocument.ActiveView.Id).WhereElementIsNotElementType().Where(Isf.ElectricalDataSource.IsAllowedCategory).ToList();
		List<ElementId> missingAreaIds = (from element in candidates
			where IsMissingParameter(element, "E_Area")
			select element.Id).Distinct().ToList();
		List<ElementId> missingWireIds = (from element in candidates
			where IsMissingParameter(element, "E_Wire")
			select element.Id).Distinct().ToList();
		List<ElementId> missingLoadNameIds = (from element in candidates
			where IsMissingParameter(element, "E_LoadName")
			select element.Id).Distinct().ToList();
		if (missingAreaIds.Count == 0 && missingWireIds.Count == 0 && missingLoadNameIds.Count == 0)
		{
			TaskDialog.Show("Object Detection", "Không có phần tử thiếu E_Area, E_Wire, E_LoadName trong ActiveView.");
			return;
		}
		TaskDialog dialog = new TaskDialog("Object Detection")
		{
			MainInstruction = "Chọn danh sách cần selection",
			MainContent = "Lệnh đã quét xong trong ActiveView. Chọn 1 list để thay thế selection hiện tại.",
			CommonButtons = TaskDialogCommonButtons.Cancel,
			AllowCancellation = true
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Thiếu E_Area ({missingAreaIds.Count})");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Thiếu E_Wire ({missingWireIds.Count})");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"Thiếu E_LoadName ({missingLoadNameIds.Count})");
		switch (dialog.Show())
		{
		case TaskDialogResult.CommandLink1:
			uiDocument.Selection.SetElementIds(missingAreaIds);
			break;
		case TaskDialogResult.CommandLink2:
			uiDocument.Selection.SetElementIds(missingWireIds);
			break;
		case TaskDialogResult.CommandLink3:
			uiDocument.Selection.SetElementIds(missingLoadNameIds);
			break;
		}
	}

	private static bool IsMissingParameter(Element element, string parameterName)
	{
		Parameter parameter = element.LookupParameter(parameterName);
		if (parameter == null || !parameter.HasValue)
		{
			return true;
		}
		if (parameter.StorageType == StorageType.String)
		{
			return string.IsNullOrWhiteSpace(parameter.AsString());
		}
		return false;
	}
}
