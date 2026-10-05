using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets.DuplicateSheets;

public static class SheetDuplicator
{
	private const int RenameRetryLimit = 25;

	private static readonly char[] ForbiddenCharacters = "\\:{}[]|;<>?`~".ToCharArray();

	public static SheetDuplicateReport Duplicate(Document document, IList<ViewSheet> sourceSheets, SheetDuplicateOptions options)
	{
		SheetDuplicateReport report = new SheetDuplicateReport();
		Dictionary<ElementId, ElementId> templateMap = new Dictionary<ElementId, ElementId>();
		using (TransactionGroup group = new TransactionGroup(document, "Duplicate Sheets"))
		{
			group.Start();
			foreach (ViewSheet sourceSheet in sourceSheets)
			{
				using Transaction transaction = new Transaction(document, "Duplicate Sheet " + sourceSheet.SheetNumber);
				try
				{
					transaction.Start();
					FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
					failureOptions.SetFailuresPreprocessor(new CopyPasteWarningSwallower());
					transaction.SetFailureHandlingOptions(failureOptions);
					ViewSheet newSheet = DuplicateSingleSheet(document, sourceSheet, options, templateMap);
					report.CreatedSheetCount++;
					report.Messages.Add("OK: " + sourceSheet.SheetNumber + " -> " + newSheet.SheetNumber);
					transaction.Commit();
				}
				catch (Exception ex)
				{
					if (transaction.HasStarted())
					{
						transaction.RollBack();
					}
					report.FailedSheetCount++;
					report.Messages.Add("FAIL: " + sourceSheet.SheetNumber + " - " + ex.Message);
				}
			}
			group.Assimilate();
		}
		return report;
	}

	private static ViewSheet DuplicateSingleSheet(Document document, ViewSheet sourceSheet, SheetDuplicateOptions options, IDictionary<ElementId, ElementId> templateMap)
	{
		List<FamilyInstance> sourceTitleBlocks = GetTitleBlocksOnSheet(document, sourceSheet);
		ElementId mainTitleBlockTypeId = ((sourceTitleBlocks.Count == 0) ? ElementId.InvalidElementId : sourceTitleBlocks[0].GetTypeId());
		ViewSheet newSheet = ViewSheet.Create(document, mainTitleBlockTypeId);
		if (options.SheetParametersToCopy.Count > 0)
		{
			CopySheetParameters(sourceSheet, newSheet, options.SheetParametersToCopy);
		}
		SetWithRetry(options.SheetNumberRule.Apply(sourceSheet.SheetNumber), delegate(string value)
		{
			newSheet.SheetNumber = Sanitize(value);
		}, (string fallback) => fallback + "*");
		SetWithRetry(options.SheetNameRule.Apply(sourceSheet.Name), delegate(string value)
		{
			newSheet.Name = Sanitize(value);
		}, (string fallback) => fallback + "*");
		if (options.PreserveTitleBlockPosition)
		{
			PreserveMainTitleBlockPosition(document, sourceTitleBlocks, newSheet);
		}
		if (sourceTitleBlocks.Count > 1)
		{
			CopyAdditionalTitleBlocks(document, sourceSheet, newSheet, (from x in sourceTitleBlocks.Skip(1)
				select x.Id).ToList());
		}
		if (options.DuplicateViews)
		{
			DuplicateViews(document, sourceSheet, newSheet, options, templateMap);
		}
		if (options.DuplicateLegends)
		{
			DuplicateLegends(document, sourceSheet, newSheet, options);
		}
		if (options.DuplicateSchedules)
		{
			DuplicateSchedules(document, sourceSheet, newSheet, options);
		}
		if (options.DuplicateImages)
		{
			CopyCategory(document, sourceSheet, newSheet, BuiltInCategory.OST_RasterImages);
		}
		if (options.DuplicateLines)
		{
			CopyCategory(document, sourceSheet, newSheet, BuiltInCategory.OST_Lines);
		}
		if (options.DuplicateText)
		{
			CopyCategory(document, sourceSheet, newSheet, BuiltInCategory.OST_TextNotes);
		}
		if (options.DuplicateClouds)
		{
			CopyCloudsAndTags(document, sourceSheet, newSheet);
		}
		if (options.DuplicateDwgs)
		{
			CopyClass(document, sourceSheet, newSheet, typeof(ImportInstance));
		}
		if (options.DuplicateSymbols)
		{
			CopyCategory(document, sourceSheet, newSheet, BuiltInCategory.OST_GenericAnnotation);
		}
		if (options.DuplicateDimensions)
		{
			CopyCategory(document, sourceSheet, newSheet, BuiltInCategory.OST_Dimensions);
		}
		if (options.DuplicateAdditionalRevisions)
		{
			ICollection<ElementId> revisionIds = sourceSheet.GetAdditionalRevisionIds();
			newSheet.SetAdditionalRevisionIds(revisionIds);
		}
		return newSheet;
	}

	private static void DuplicateViews(Document document, ViewSheet sourceSheet, ViewSheet newSheet, SheetDuplicateOptions options, IDictionary<ElementId, ElementId> templateMap)
	{
		foreach (ElementId viewportId in sourceSheet.GetAllViewports())
		{
			if (!(document.GetElement(viewportId) is Viewport sourceViewport) || !(document.GetElement(sourceViewport.ViewId) is View { ViewType: not ViewType.Legend } sourceView))
			{
				continue;
			}
			ViewDuplicateOption duplicateOption = options.ViewDuplicateOption;
			if (sourceView.ViewType == ViewType.ThreeD && duplicateOption == ViewDuplicateOption.AsDependent)
			{
				duplicateOption = ViewDuplicateOption.Duplicate;
			}
			ElementId newViewId = sourceView.Duplicate(duplicateOption);
			Element element = document.GetElement(newViewId);
			View newView = element as View;
			if (newView == null)
			{
				continue;
			}
			ApplyViewTemplateMode(document, sourceView, newView, options.ViewTemplateMode, templateMap);
			SetWithRetry(options.ViewNameRule.Apply(sourceView.Name), delegate(string value)
			{
				newView.Name = Sanitize(value);
			}, (string fallback) => fallback + "*");
			if (Viewport.CanAddViewToSheet(document, newSheet.Id, newView.Id))
			{
				XYZ center = sourceViewport.GetBoxCenter();
				Viewport newViewport = Viewport.Create(document, newSheet.Id, newView.Id, center);
				ElementId sourceViewportTypeId = sourceViewport.GetTypeId();
				if (newViewport.GetTypeId() != sourceViewportTypeId)
				{
					newViewport.ChangeTypeId(sourceViewportTypeId);
				}
				TryCopyViewportLabelLineLength(sourceViewport, newViewport);
				TryMatchViewportLabelPosition(sourceViewport, newViewport);
			}
		}
	}

	private static void DuplicateLegends(Document document, ViewSheet sourceSheet, ViewSheet newSheet, SheetDuplicateOptions options)
	{
		foreach (ElementId viewportId in sourceSheet.GetAllViewports())
		{
			if (!(document.GetElement(viewportId) is Viewport sourceViewport) || !(document.GetElement(sourceViewport.ViewId) is View { ViewType: ViewType.Legend } sourceLegend))
			{
				continue;
			}
			View targetLegend = sourceLegend;
			if (!options.UseExistingLegends)
			{
				ElementId duplicatedLegendId = sourceLegend.Duplicate(ViewDuplicateOption.WithDetailing);
				if (document.GetElement(duplicatedLegendId) is View duplicatedLegend)
				{
					targetLegend = duplicatedLegend;
				}
			}
			if (Viewport.CanAddViewToSheet(document, newSheet.Id, targetLegend.Id))
			{
				Viewport newViewport = Viewport.Create(document, newSheet.Id, targetLegend.Id, sourceViewport.GetBoxCenter());
				ElementId sourceViewportTypeId = sourceViewport.GetTypeId();
				if (newViewport.GetTypeId() != sourceViewportTypeId)
				{
					newViewport.ChangeTypeId(sourceViewportTypeId);
				}
				TryCopyViewportLabelLineLength(sourceViewport, newViewport);
				TryMatchViewportLabelPosition(sourceViewport, newViewport);
			}
		}
	}

	private static void DuplicateSchedules(Document document, ViewSheet sourceSheet, ViewSheet newSheet, SheetDuplicateOptions options)
	{
		IEnumerable<ScheduleSheetInstance> scheduleInstances = new FilteredElementCollector(document, sourceSheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>();
		foreach (ScheduleSheetInstance sourceScheduleInstance in scheduleInstances)
		{
			if (sourceScheduleInstance.IsTitleblockRevisionSchedule)
			{
				continue;
			}
			ElementId sourceScheduleViewId = sourceScheduleInstance.ScheduleId;
			if (!(sourceScheduleViewId == ElementId.InvalidElementId))
			{
				ElementId targetScheduleViewId = sourceScheduleViewId;
				if (!options.UseExistingSchedules && document.GetElement(sourceScheduleViewId) is ViewSchedule sourceScheduleView)
				{
					targetScheduleViewId = sourceScheduleView.Duplicate(ViewDuplicateOption.Duplicate);
				}
				ScheduleSheetInstance.Create(document, newSheet.Id, targetScheduleViewId, sourceScheduleInstance.Point);
			}
		}
	}

	private static void CopyCategory(Document document, ViewSheet sourceSheet, ViewSheet newSheet, BuiltInCategory category)
	{
		ICollection<ElementId> ids = new FilteredElementCollector(document, sourceSheet.Id).OfCategory(category).WhereElementIsNotElementType().ToElementIds();
		CopyElements(sourceSheet, newSheet, ids);
	}

	private static void CopyClass(Document document, ViewSheet sourceSheet, ViewSheet newSheet, Type classType)
	{
		ICollection<ElementId> ids = new FilteredElementCollector(document, sourceSheet.Id).OfClass(classType).WhereElementIsNotElementType().ToElementIds();
		CopyElements(sourceSheet, newSheet, ids);
	}

	private static void CopyCloudsAndTags(Document document, ViewSheet sourceSheet, ViewSheet newSheet)
	{
		List<BuiltInCategory> categories = new List<BuiltInCategory>
		{
			BuiltInCategory.OST_RevisionClouds,
			BuiltInCategory.OST_RevisionCloudTags
		};
		ElementMulticategoryFilter filter = new ElementMulticategoryFilter(categories);
		ICollection<ElementId> ids = new FilteredElementCollector(document, sourceSheet.Id).WherePasses(filter).WhereElementIsNotElementType().ToElementIds();
		CopyElements(sourceSheet, newSheet, ids);
	}

	private static void CopyAdditionalTitleBlocks(Document document, ViewSheet sourceSheet, ViewSheet newSheet, IList<ElementId> extraTitleBlockIds)
	{
		if (extraTitleBlockIds.Count != 0)
		{
			List<ElementId> list = new List<ElementId>(extraTitleBlockIds);
			CopyElements(sourceSheet, newSheet, list);
		}
	}

	private static void CopyElements(View sourceView, View destinationView, ICollection<ElementId> elementIds)
	{
		if (elementIds.Count != 0)
		{
			List<ElementId> list = new List<ElementId>(elementIds);
			ElementTransformUtils.CopyElements(sourceView, list, destinationView, null, new CopyPasteOptions());
		}
	}

	private static void PreserveMainTitleBlockPosition(Document document, IList<FamilyInstance> sourceTitleBlocks, ViewSheet newSheet)
	{
		if (sourceTitleBlocks.Count == 0)
		{
			return;
		}
		List<FamilyInstance> targetTitleBlocks = GetTitleBlocksOnSheet(document, newSheet);
		if (targetTitleBlocks.Count == 0)
		{
			return;
		}
		FamilyInstance source = sourceTitleBlocks[0];
		FamilyInstance target = targetTitleBlocks[0];
		if (source.Location is LocationPoint sourceLocation && target.Location is LocationPoint targetLocation)
		{
			XYZ delta = sourceLocation.Point - targetLocation.Point;
			if (!(delta.GetLength() < 1E-09))
			{
				ElementTransformUtils.MoveElement(document, target.Id, delta);
			}
		}
	}

	private static List<FamilyInstance> GetTitleBlocksOnSheet(Document document, ViewSheet sheet)
	{
		return (from x in new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>()
			orderby x.Id.Value
			select x).ToList();
	}

	private static void ApplyViewTemplateMode(Document document, View sourceView, View newView, SheetViewTemplateMode mode, IDictionary<ElementId, ElementId> templateMap)
	{
		if (mode == SheetViewTemplateMode.Keep)
		{
			return;
		}
		ElementId sourceTemplateId = sourceView.ViewTemplateId;
		if (!(sourceTemplateId == ElementId.InvalidElementId))
		{
			ElementId existingTemplateId;
			if (mode == SheetViewTemplateMode.Remove)
			{
				newView.ViewTemplateId = ElementId.InvalidElementId;
			}
			else if (templateMap.TryGetValue(sourceTemplateId, out existingTemplateId))
			{
				newView.ViewTemplateId = existingTemplateId;
			}
			else if (document.GetElement(sourceTemplateId) is View sourceTemplateView)
			{
				ElementId duplicatedTemplateId = (templateMap[sourceTemplateId] = sourceTemplateView.Duplicate(ViewDuplicateOption.Duplicate));
				newView.ViewTemplateId = duplicatedTemplateId;
			}
		}
	}

	private static void CopySheetParameters(ViewSheet sourceSheet, ViewSheet targetSheet, IList<string> parameterNames)
	{
		foreach (string parameterName in parameterNames)
		{
			if (string.IsNullOrWhiteSpace(parameterName))
			{
				continue;
			}
			Parameter sourceParameter = sourceSheet.LookupParameter(parameterName);
			Parameter targetParameter = targetSheet.LookupParameter(parameterName);
			if (sourceParameter == null || targetParameter == null || targetParameter.IsReadOnly || IsProtectedSheetParameter(sourceParameter) || IsProtectedSheetParameter(targetParameter) || sourceParameter.StorageType != targetParameter.StorageType)
			{
				continue;
			}
			try
			{
				switch (sourceParameter.StorageType)
				{
				case StorageType.String:
					targetParameter.Set(sourceParameter.AsString() ?? string.Empty);
					break;
				case StorageType.Integer:
					targetParameter.Set(sourceParameter.AsInteger());
					break;
				case StorageType.Double:
					targetParameter.Set(sourceParameter.AsDouble());
					break;
				case StorageType.ElementId:
					targetParameter.Set(sourceParameter.AsElementId());
					break;
				}
			}
			catch
			{
			}
		}
	}

	private static bool IsProtectedSheetParameter(Parameter parameter)
	{
		ElementId id = parameter.Id;
		return id == new ElementId(-1007401L);
	}

	private static void SetWithRetry(string initialValue, Action<string> setter, Func<string, string> fallback)
	{
		string candidate = Sanitize(initialValue);
		if (string.IsNullOrWhiteSpace(candidate))
		{
			candidate = "unnamed";
		}
		for (int i = 0; i < 25; i++)
		{
			try
			{
				setter(candidate);
				break;
			}
			catch
			{
				candidate = fallback(candidate);
			}
		}
	}

	private static string Sanitize(string value)
	{
		string result = value ?? string.Empty;
		char[] forbiddenCharacters = ForbiddenCharacters;
		foreach (char forbiddenCharacter in forbiddenCharacters)
		{
			result = result.Replace(forbiddenCharacter.ToString(), string.Empty);
		}
		return string.IsNullOrWhiteSpace(result) ? "unnamed" : result.Trim();
	}

	private static void TryMatchViewportLabelPosition(Viewport sourceViewport, Viewport targetViewport)
	{
		try
		{
			Outline sourceOutline = sourceViewport.GetLabelOutline();
			Outline targetOutline = targetViewport.GetLabelOutline();
			XYZ sourceAnchor = sourceOutline.MinimumPoint;
			XYZ targetAnchor = targetOutline.MinimumPoint;
			XYZ delta = sourceAnchor - targetAnchor;
			if (delta.GetLength() < 1E-09)
			{
				return;
			}
			Type targetType = targetViewport.GetType();
			PropertyInfo labelOffsetProperty = targetType.GetProperty("LabelOffset");
			if (labelOffsetProperty != null && labelOffsetProperty.CanWrite)
			{
				object current = labelOffsetProperty.GetValue(targetViewport, null);
				if (current is XYZ currentOffset)
				{
					labelOffsetProperty.SetValue(targetViewport, currentOffset + delta, null);
				}
				else
				{
					labelOffsetProperty.SetValue(targetViewport, delta, null);
				}
				return;
			}
			MethodInfo setLabelOffsetMethod = targetType.GetMethod("SetLabelOffset", new Type[1] { typeof(XYZ) });
			if (setLabelOffsetMethod != null)
			{
				setLabelOffsetMethod.Invoke(targetViewport, new object[1] { delta });
			}
		}
		catch
		{
		}
	}

	private static void TryCopyViewportLabelLineLength(Viewport sourceViewport, Viewport targetViewport)
	{
		try
		{
			Type viewportType = sourceViewport.GetType();
			PropertyInfo property = viewportType.GetProperty("LabelLineLength");
			if (property != null && property.CanRead && property.CanWrite && property.GetValue(sourceViewport, null) is double length)
			{
				property.SetValue(targetViewport, length, null);
			}
			else if (!TryCopyViewportLabelLengthByParameter(sourceViewport, targetViewport, "Label Line Length"))
			{
				TryCopyViewportLabelLengthByParameter(sourceViewport, targetViewport, "LabelLineLength");
			}
		}
		catch
		{
		}
	}

	private static bool TryCopyViewportLabelLengthByParameter(Viewport sourceViewport, Viewport targetViewport, string parameterName)
	{
		Parameter sourceParameter = sourceViewport.LookupParameter(parameterName);
		Parameter targetParameter = targetViewport.LookupParameter(parameterName);
		if (sourceParameter == null || targetParameter == null)
		{
			return false;
		}
		if (sourceParameter.StorageType != StorageType.Double || targetParameter.StorageType != StorageType.Double)
		{
			return false;
		}
		if (targetParameter.IsReadOnly)
		{
			return false;
		}
		targetParameter.Set(sourceParameter.AsDouble());
		return true;
	}
}
