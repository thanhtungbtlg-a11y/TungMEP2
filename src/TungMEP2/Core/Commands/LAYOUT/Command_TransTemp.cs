using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_TransTemp : SecuredExternalCommand
{
	private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
	{
		public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
		{
			return DuplicateTypeAction.UseDestinationTypes;
		}
	}

	private const string DialogTitle = "Transfer View Template";

	protected override void ExecuteCore()
	{
		try
		{
			List<TransTempLinkItem> links = CollectLinkItems(base.Document);
			if (links.Count == 0)
			{
				TaskDialog.Show("Transfer View Template", "No loaded Revit links found in the active model.");
				return;
			}
			UI_TransTempWindow window = new UI_TransTempWindow(links);
			if (window.ShowDialog() != true || window.SelectedLinkDocument == null || window.SelectedTemplateIds.Count == 0)
			{
				return;
			}
			Document sourceDocument = window.SelectedLinkDocument;
			ICollection<ElementId> sourceTemplateIds = window.SelectedTemplateIds.ToList();
			using Transaction transaction = new Transaction(base.Document, "Transfer View Templates");
			transaction.Start();
			CopyPasteOptions copyOptions = new CopyPasteOptions();
			copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
			ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(sourceDocument, sourceTemplateIds, base.Document, Transform.Identity, copyOptions);
			int transferred = 0;
			int filtersRemoved = 0;
			foreach (ElementId copiedId in copiedIds)
			{
				if (!(base.Document.GetElement(copiedId) is View { IsTemplate: not false } template))
				{
					continue;
				}
				transferred++;
				if (window.IncludeFilters)
				{
					continue;
				}
				foreach (ElementId filterId in template.GetFilters())
				{
					try
					{
						template.RemoveFilter(filterId);
						filtersRemoved++;
					}
					catch
					{
					}
				}
			}
			transaction.Commit();
			string report = $"Source templates: {window.SelectedTemplateIds.Count}\n" + $"Transferred templates: {transferred}";
			if (!window.IncludeFilters)
			{
				report += $"\nRemoved filters: {filtersRemoved}";
			}
			TaskDialog.Show("Transfer View Template", report);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Transfer View Template", ex2.Message);
		}
	}

	private static List<TransTempLinkItem> CollectLinkItems(Document document)
	{
		List<RevitLinkInstance> collector = new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
		Dictionary<string, int> countsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		List<TransTempLinkItem> result = new List<TransTempLinkItem>();
		foreach (RevitLinkInstance instance in collector)
		{
			Document linkDocument = instance.GetLinkDocument();
			if (linkDocument != null)
			{
				string rawName = (instance.Name ?? string.Empty).Trim();
				string baseName = rawName.Split(':')[0].Trim();
				if (string.IsNullOrWhiteSpace(baseName))
				{
					baseName = "Link";
				}
				countsByName.TryGetValue(baseName, out var duplicateIndex);
				countsByName[baseName] = duplicateIndex + 1;
				string displayName = ((duplicateIndex == 0) ? baseName : $"{baseName} ({duplicateIndex})");
				List<TransTempTemplateItem> templates = CollectTemplates(linkDocument);
				result.Add(new TransTempLinkItem(displayName, linkDocument, templates));
			}
		}
		return result.OrderBy<TransTempLinkItem, string>((TransTempLinkItem item) => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<TransTempTemplateItem> CollectTemplates(Document linkDocument)
	{
		return (from view in (from View view in new FilteredElementCollector(linkDocument).OfClass(typeof(View))
				where view.IsTemplate
				select view).OrderBy((View view) => view.Name, StringComparer.OrdinalIgnoreCase)
			select new TransTempTemplateItem(view.Id, view.Name ?? string.Empty)).ToList();
	}
}
