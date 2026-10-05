using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Electrical;

internal static class SimilarRunner
{
	private const string DialogTitle = "Similar";

	private const string ParameterArea = "E_Area";

	private const string ParameterLoadName = "E_LoadName";

	private const string ParameterWire = "E_Wire";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		string parameterName = PromptParameterName();
		if (string.IsNullOrWhiteSpace(parameterName))
		{
			return;
		}
		string selectedParameterName = parameterName;
		Reference sourceReference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.ElectricalDataSource(), "Chọn phần tử nguồn để tìm các phần tử có cùng " + selectedParameterName + " trong view hiện tại");
		Element sourceElement = document.GetElement(sourceReference);
		string sourceValue = GetComparableValue(sourceElement, selectedParameterName);
		if (string.IsNullOrWhiteSpace(sourceValue))
		{
			TaskDialog.Show("Similar", "Phần tử nguồn không có giá trị " + selectedParameterName + ".");
			return;
		}
		List<ElementId> matchedIds = (from element in new FilteredElementCollector(document, uiDocument.ActiveView.Id).WhereElementIsNotElementType().Where(Isf.ElectricalDataSource.IsAllowedCategory)
			where string.Equals(GetComparableValue(element, selectedParameterName), sourceValue, StringComparison.Ordinal)
			select element.Id).Distinct().ToList();
		if (matchedIds.Count != 0)
		{
			uiDocument.Selection.SetElementIds(matchedIds);
		}
	}

	private static string? PromptParameterName()
	{
		TaskDialog dialog = new TaskDialog("Similar")
		{
			MainInstruction = "Chọn parameter để tìm Similar",
			MainContent = "Chọn 1 giá trị chung: E_Area, E_LoadName hoặc E_Wire.",
			CommonButtons = TaskDialogCommonButtons.Cancel,
			AllowCancellation = true
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "E_Area");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "E_LoadName");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "E_Wire");
		return dialog.Show() switch
		{
			TaskDialogResult.CommandLink1 => "E_Area", 
			TaskDialogResult.CommandLink2 => "E_LoadName", 
			TaskDialogResult.CommandLink3 => "E_Wire", 
			_ => null, 
		};
	}

	private static string? GetComparableValue(Element? element, string parameterName)
	{
		if (element == null)
		{
			return null;
		}
		Parameter parameter = element.LookupParameter(parameterName);
		if (parameter == null || !parameter.HasValue)
		{
			return null;
		}
		if (parameter.StorageType == StorageType.String)
		{
			return parameter.AsString();
		}
		return parameter.AsValueString();
	}
}
