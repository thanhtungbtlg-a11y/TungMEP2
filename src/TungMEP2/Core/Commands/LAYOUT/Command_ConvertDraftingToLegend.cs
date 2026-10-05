using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Sheets.DuplicateSheets;
using VP_RevitAPI.Helper.Views;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_ConvertDraftingToLegend : SecuredExternalCommand
{
	private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
	{
		public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
		{
			return DuplicateTypeAction.UseDestinationTypes;
		}
	}

	private const string DialogTitle = "Convert Drafting To Legend";

	protected override void ExecuteCore()
	{
		try
		{
			List<View> draftingViews = CollectViews(base.Document, ViewType.DraftingView);
			List<View> legendViews = CollectViews(base.Document, ViewType.Legend);
			if (legendViews.Count == 0)
			{
				TaskDialog.Show("Convert Drafting To Legend", "At least one Legend view must exist in the model.");
				return;
			}
			if (draftingViews.Count == 0)
			{
				TaskDialog.Show("Convert Drafting To Legend", "No Drafting Views found in the model.");
				return;
			}
			List<ElementId> preselectedDraftingViewIds = (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id) as View into view
				where view != null && view.ViewType == ViewType.DraftingView && !view.IsTemplate
				select view.Id).Distinct().ToList();
			View baseLegend = legendViews[0];
			UI_ConvertDraftingToLegendWindow window = new UI_ConvertDraftingToLegendWindow(draftingViews, preselectedDraftingViewIds);
			if (window.ShowDialog() != true || window.SelectedDraftingViewIds.Count == 0)
			{
				return;
			}
			HashSet<string> existingLegendNames = new HashSet<string>(legendViews.Select((View v) => v.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
			int success = 0;
			int skipped = 0;
			int failed = 0;
			List<string> lines = new List<string>();
			using (TransactionGroup group = new TransactionGroup(base.Document, "Convert Drafting To Legend"))
			{
				group.Start();
				foreach (ElementId draftingId in window.SelectedDraftingViewIds)
				{
					if (base.Document.GetElement(draftingId) is View { ViewType: ViewType.DraftingView } srcDrafting)
					{
						IList<ElementId> idsToCopy = CollectCopyableElementIds(base.Document, srcDrafting);
						if (idsToCopy.Count == 0)
						{
							skipped++;
							lines.Add("SKIP: " + srcDrafting.Name + " - Empty / no copyable elements.");
							continue;
						}
						using Transaction tx = new Transaction(base.Document, "Convert " + srcDrafting.Name);
						try
						{
							tx.Start();
							FailureHandlingOptions failureHandling = tx.GetFailureHandlingOptions();
							failureHandling.SetFailuresPreprocessor(new CopyPasteWarningSwallower());
							tx.SetFailureHandlingOptions(failureHandling);
							ElementId newLegendId = baseLegend.Duplicate(ViewDuplicateOption.Duplicate);
							if (!(base.Document.GetElement(newLegendId) is View { ViewType: ViewType.Legend } destLegend))
							{
								throw new System.InvalidOperationException("Failed to create destination legend.");
							}
							CopyPasteOptions copyOptions = new CopyPasteOptions();
							copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
							ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(srcDrafting, idsToCopy, destLegend, null, copyOptions);
							if (window.CopyOverrides)
							{
								ApplyOverridesBestEffort(srcDrafting, destLegend, idsToCopy, copiedIds);
							}
							string desiredName = srcDrafting.Name ?? string.Empty;
							string uniqueName = (destLegend.Name = DraftingToLegendNaming.BuildUniqueLegendName(existingLegendNames, desiredName));
							if (window.CopyScale)
							{
								try
								{
									destLegend.Scale = srcDrafting.Scale;
								}
								catch
								{
								}
							}
							tx.Commit();
							success++;
							lines.Add("OK: " + srcDrafting.Name + " -> " + uniqueName);
						}
						catch (Autodesk.Revit.Exceptions.OperationCanceledException)
						{
							if (tx.HasStarted())
							{
								tx.RollBack();
							}
							group.RollBack();
							return;
						}
						catch (Exception ex2)
						{
							if (tx.HasStarted())
							{
								tx.RollBack();
							}
							failed++;
							lines.Add("FAIL: " + srcDrafting.Name + " - " + ex2.Message);
						}
					}
					else
					{
						skipped++;
						lines.Add($"SKIP: [{draftingId.Value}] - Not a Drafting View.");
					}
				}
				group.Assimilate();
			}
			TaskDialog.Show("Convert Drafting To Legend", $"Success: {success}\n" + $"Skipped: {skipped}\n" + $"Failed: {failed}\n\n" + string.Join("\n", lines));
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex4)
		{
			TaskDialog.Show("Convert Drafting To Legend", ex4.Message);
		}
	}

	private static List<View> CollectViews(Document document, ViewType viewType)
	{
		if (document == null)
		{
			return new List<View>();
		}
		return (from View view in new FilteredElementCollector(document).OfClass(typeof(View))
			where view.ViewType == viewType && !view.IsTemplate
			orderby view.Name
			select view).ToList();
	}

	private static IList<ElementId> CollectCopyableElementIds(Document document, View draftingView)
	{
		IList<Element> elements = new FilteredElementCollector(document, draftingView.Id).WhereElementIsNotElementType().ToElements();
		List<ElementId> ids = new List<ElementId>(elements.Count);
		foreach (Element element in elements)
		{
			if (element != null && element.Category != null)
			{
				ids.Add(element.Id);
			}
		}
		return ids;
	}

	private static void ApplyOverridesBestEffort(View srcView, View destView, IList<ElementId> sourceIds, ICollection<ElementId> copiedIds)
	{
		if (sourceIds.Count == 0 || copiedIds.Count == 0)
		{
			return;
		}
		List<ElementId> copiedList = copiedIds.ToList();
		int count = Math.Min(sourceIds.Count, copiedList.Count);
		for (int i = 0; i < count; i++)
		{
			try
			{
				ElementId sourceId = sourceIds[i];
				ElementId destinationId = copiedList[i];
				OverrideGraphicSettings settings = srcView.GetElementOverrides(sourceId);
				destView.SetElementOverrides(destinationId, settings);
			}
			catch
			{
			}
		}
	}
}
