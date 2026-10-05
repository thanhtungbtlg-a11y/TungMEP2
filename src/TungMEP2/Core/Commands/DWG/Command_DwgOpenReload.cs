using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.DWG;

[Transaction(TransactionMode.Manual)]
public class Command_DwgOpenReload : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ImportInstance importInstance = GetSelectedImportInstance();
			if (importInstance == null)
			{
				TaskDialog.Show("DWG: Open/Reload", "Select a DWG ImportInstance and run the command again.");
				return;
			}
			if (!importInstance.IsLinked)
			{
				TaskDialog.Show("DWG: Open/Reload", "Selected DWG instance is not linked.");
				return;
			}
			CADLinkType cadLinkType = GetCadLinkType(importInstance);
			if (cadLinkType == null)
			{
				TaskDialog.Show("DWG: Open/Reload", "Could not resolve CAD link type from the selected DWG instance.");
				return;
			}
			if (!TryGetDwgPath(cadLinkType, out string dwgPath))
			{
				TaskDialog.Show("DWG: Open/Reload", "Could not resolve DWG file path from the selected link.");
				return;
			}
			UI_DwgOpenReloadWindow window = new UI_DwgOpenReloadWindow(base.Document, cadLinkType.Id, dwgPath);
			window.ShowDialog();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("DWG: Open/Reload", ex2.ToString());
		}
	}

	private ImportInstance? GetSelectedImportInstance()
	{
		foreach (ElementId elementId in base.UiDocument.Selection.GetElementIds())
		{
			if (base.Document.GetElement(elementId) is ImportInstance importInstance)
			{
				return importInstance;
			}
		}
		return null;
	}

	private CADLinkType? GetCadLinkType(ImportInstance importInstance)
	{
		if (base.Document.GetElement(importInstance.GetTypeId()) is CADLinkType typedByTypeId)
		{
			return typedByTypeId;
		}
		ElementId fallbackTypeId = ((Element)importInstance).get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsElementId();
		if (fallbackTypeId != null && fallbackTypeId != ElementId.InvalidElementId)
		{
			return base.Document.GetElement(fallbackTypeId) as CADLinkType;
		}
		return null;
	}

	private static bool TryGetDwgPath(CADLinkType cadLinkType, out string dwgPath)
	{
		dwgPath = string.Empty;
		ExternalFileReference externalFileReference = cadLinkType.GetExternalFileReference();
		if (externalFileReference == null)
		{
			return false;
		}
		ModelPath modelPath = externalFileReference.GetAbsolutePath();
		if (modelPath == null)
		{
			modelPath = externalFileReference.GetPath();
		}
		if (modelPath == null)
		{
			return false;
		}
		dwgPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
		return !string.IsNullOrWhiteSpace(dwgPath);
	}
}
