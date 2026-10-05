using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelLegendSyncService
{
	public static ExcelLegendLinkSummary CheckAndUpdate(Document document)
	{
		return CheckAndUpdateInternal(document, null);
	}

	public static ExcelLegendLinkSummary CheckAndUpdateActiveLegend(Document document, ElementId activeLegendId)
	{
		if (!(document.GetElement(activeLegendId) is View { ViewType: ViewType.Legend }))
		{
			List<ExcelLegendLinkItemResult> invalidItems = new List<ExcelLegendLinkItemResult>
			{
				new ExcelLegendLinkItemResult(null, string.Empty, string.Empty, string.Empty, ExcelLegendLinkAction.Failed, "Active legend is invalid.", IsWarning: true)
			};
			return new ExcelLegendLinkSummary(0, 0, 0, 0, 0, 0, 0, 1, invalidItems);
		}
		return CheckAndUpdateInternal(document, activeLegendId);
	}

	private static ExcelLegendLinkSummary CheckAndUpdateInternal(Document document, ElementId? targetLegendId)
	{
		List<ExcelLegendLinkItemResult> items = new List<ExcelLegendLinkItemResult>();
		int scanned = 0;
		int updated = 0;
		int unchanged = 0;
		int staleFound = 0;
		int orphaned = 0;
		int parseError = 0;
		int failed = 0;
		if (!ExcelLegendLinkMetadata.EnsureParameters(document, out string parameterError))
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, string.Empty, string.Empty, ExcelLegendLinkAction.Failed, "Cannot ensure VP_Excel_* parameters: " + parameterError, IsWarning: true));
			return new ExcelLegendLinkSummary(0, 0, 0, 0, 0, 0, 0, 1, items);
		}
		if (!OptionalFunctionalityUtils.IsPDFImportAvailable())
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, string.Empty, string.Empty, ExcelLegendLinkAction.Failed, "PDF import không khả dụng trong bản cài Revit hiện tại.", IsWarning: true));
			return new ExcelLegendLinkSummary(0, 0, 0, 0, 0, 0, 0, 1, items);
		}
		List<ExcelLegendLinkService.LinkedImageInfo> linkedImages = CollectLinkedImages(document, items, ref parseError, targetLegendId);
		scanned = linkedImages.Count;
		if (scanned == 0)
		{
			return new ExcelLegendLinkSummary(0, 0, 0, 0, 0, 0, parseError, 0, items);
		}
		Dictionary<string, HashSet<string>> definedNameSetCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ExcelDefinedNameExportSummary> exportCache = new Dictionary<string, ExcelDefinedNameExportSummary>(StringComparer.OrdinalIgnoreCase);
		using TransactionGroup group = new TransactionGroup(document, "Check and update Excel Legend links");
		group.Start();
		foreach (ExcelLegendLinkService.LinkedImageInfo linked in linkedImages)
		{
			string excelPath = linked.ExcelPath;
			string legendName = linked.Legend.Name ?? string.Empty;
			string displayDefinedName = linked.Metadata.ScopedDefinedName;
			try
			{
				if (!File.Exists(excelPath))
				{
					failed++;
					items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, displayDefinedName, ExcelLegendLinkAction.Failed, "Excel file not found.", IsWarning: true));
					continue;
				}
				if (!definedNameSetCache.TryGetValue(excelPath, out var names))
				{
					names = (definedNameSetCache[excelPath] = LoadDefinedNames(excelPath));
				}
				long currentLastWriteTicks = File.GetLastWriteTimeUtc(excelPath).Ticks;
				long currentImageLastWriteTicks;
				long currentImageFileSize;
				string error;
				bool hasImageStamp = ExcelLegendLinkService.TryGetLinkedImageFileStamp(document, linked.Image, out currentImageLastWriteTicks, out currentImageFileSize, out error);
				string targetSheetName = linked.Metadata.SheetName;
				if (string.IsNullOrWhiteSpace(targetSheetName))
				{
					failed++;
					items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, displayDefinedName, ExcelLegendLinkAction.Failed, "Legacy link is missing sheet scope; vui lòng relink từ cửa sổ Link Sync.", IsWarning: true));
					continue;
				}
				string targetDefinedName = linked.Metadata.DefinedName;
				string targetScopedDefinedName = linked.Metadata.ScopedDefinedName;
				bool nameExists = names.Contains(targetScopedDefinedName);
				bool needsPdfMigration = NeedsPdfMigration(document, linked.Image);
				if (!nameExists)
				{
					using (Transaction orphanTx = new Transaction(document, "Mark orphaned " + displayDefinedName))
					{
						orphanTx.Start();
						ExcelLegendLinkMetadata orphanMetadata = linked.Metadata;
						if (!ExcelLegendLinkMetadata.TryWrite(linked.Legend, orphanMetadata, out string nameError))
						{
							throw new InvalidOperationException("Cannot write orphaned metadata to VP_Excel_* parameters: " + nameError);
						}
						orphanTx.Commit();
						orphaned++;
						items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, targetScopedDefinedName, ExcelLegendLinkAction.MarkedOrphaned, "Defined name does not exist in workbook.", IsWarning: true));
					}
					continue;
				}
				if (hasImageStamp && !ExcelLegendLinkMetadata.HasSourceChanged(linked.Metadata, currentLastWriteTicks, currentImageLastWriteTicks, currentImageFileSize) && !needsPdfMigration)
				{
					unchanged++;
					items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, targetScopedDefinedName, ExcelLegendLinkAction.Unchanged, "No update needed."));
					continue;
				}
				staleFound++;
				string exportCacheKey = BuildExportCacheKey(excelPath, targetSheetName, targetDefinedName);
				if (!exportCache.TryGetValue(exportCacheKey, out var exportSummary))
				{
					exportSummary = (exportCache[exportCacheKey] = ExcelDefinedNamePdfExporter.Export(excelPath, (IReadOnlyCollection<ExcelDefinedNameExportTarget>?)(object)new ExcelDefinedNameExportTarget[1]
					{
						new ExcelDefinedNameExportTarget(targetSheetName, targetDefinedName)
					}));
				}
				ExcelDefinedNameExportItemResult exportedItem = exportSummary.Items.FirstOrDefault((ExcelDefinedNameExportItemResult item) => item.IsExported && string.Equals(item.SheetName, targetSheetName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.DefinedName, targetDefinedName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.OutputPath));
				if (exportedItem == null)
				{
					failed++;
					items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, targetScopedDefinedName, ExcelLegendLinkAction.Failed, "Cannot render updated image for stale entry.", IsWarning: true));
					continue;
				}
				using Transaction updateTx = new Transaction(document, "Update stale " + targetDefinedName);
				updateTx.Start();
				if (!ExcelLegendLinkService.TryRefreshManagedImageInPlace(document, linked.Image, exportedItem.OutputPath, out string refreshError))
				{
					throw new InvalidOperationException("Cannot refresh stale linked image: " + refreshError);
				}
				if (!ExcelLegendLinkService.TryGetFileStamp(exportedItem.OutputPath, out long imageLastWriteTicks, out long imageFileSize, out string imageStampError))
				{
					throw new InvalidOperationException("Cannot read linked image file stamp: " + imageStampError);
				}
				ExcelLegendLinkMetadata newMetadata = linked.Metadata with
				{
					LastWriteTicks = currentLastWriteTicks,
					ImageLastWriteTicks = imageLastWriteTicks,
					ImageFileSize = imageFileSize
				};
				if (!ExcelLegendLinkMetadata.TryWrite(linked.Legend, newMetadata, out string updateNameError))
				{
					throw new InvalidOperationException("Cannot update VP_Excel_* metadata: " + updateNameError);
				}
				updateTx.Commit();
				updated++;
				items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, targetScopedDefinedName, ExcelLegendLinkAction.Updated, "Stale link updated."));
			}
			catch (Exception ex)
			{
				failed++;
				items.Add(new ExcelLegendLinkItemResult(linked.Legend.Id.Value, legendName, excelPath, displayDefinedName, ExcelLegendLinkAction.Failed, ex.Message, IsWarning: true));
			}
		}
		group.Assimilate();
		return new ExcelLegendLinkSummary(scanned, 0, updated, unchanged, staleFound, orphaned, parseError, failed, items);
	}

	internal static List<ExcelLegendLinkService.LinkedImageInfo> CollectLinkedImages(Document document, ICollection<ExcelLegendLinkItemResult> items, ref int parseError, ElementId? targetLegendId = null)
	{
		List<ExcelLegendLinkService.LinkedImageInfo> linked = new List<ExcelLegendLinkService.LinkedImageInfo>();
		foreach (View legend in from View view in new FilteredElementCollector(document).OfClass(typeof(View))
			where view.ViewType == ViewType.Legend && !view.IsTemplate && (targetLegendId == null || view.Id == targetLegendId)
			select view)
		{
			string legendName = legend.Name ?? string.Empty;
			if (!ExcelLegendLinkMetadata.TryRead(legend, out ExcelLegendLinkMetadata metadata, out string _) && !TryMigrateMetadataFromViewName(document, legend, out metadata, out string migrationError))
			{
				if (ExcelLegendLinkMetadata.IsProviderMatch(legendName))
				{
					parseError++;
					items.Add(new ExcelLegendLinkItemResult(legend.Id.Value, legendName, string.Empty, string.Empty, ExcelLegendLinkAction.Skipped, "Metadata parse error: " + migrationError, IsWarning: true));
				}
				continue;
			}
			ImageInstance image = ExcelLegendLinkService.FindManagedImageInLegend(legend);
			if (image == null)
			{
				parseError++;
				items.Add(new ExcelLegendLinkItemResult(legend.Id.Value, legendName, string.Empty, metadata.DefinedName, ExcelLegendLinkAction.Skipped, "Managed linked image not found in legend.", IsWarning: true));
			}
			else
			{
				linked.Add(new ExcelLegendLinkService.LinkedImageInfo(legend, image, metadata, metadata.WorkbookPath, metadata.ScopedDefinedName));
			}
		}
		return linked;
	}

	internal static HashSet<string> LoadDefinedNames(string excelPath)
	{
		HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using ExcelInteropSession session = ExcelInteropSession.OpenReadOnly(excelPath);
		SheetScopedNameScanner.ForEachDefinedName(session.Workbook, delegate(Worksheet? worksheet, Name name, bool isWorkbookScope)
		{
			string text = (isWorkbookScope ? "Workbook" : (worksheet?.Name ?? string.Empty));
			if (!string.IsNullOrWhiteSpace(text))
			{
				names.Add(BuildScopedNameKey(text, name.Name));
			}
		});
		return names;
	}

	private static bool TryMigrateMetadataFromViewName(Document document, View legend, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		if (!ExcelLegendLinkMetadata.TryMigrateFromLegacy(legend, out ExcelLegendLinkMetadata migrated, out string migrateError) || migrated == null)
		{
			error = migrateError;
			return false;
		}
		using Transaction tx = new Transaction(document, "Migrate Excel metadata to VP_ExcelLink");
		try
		{
			tx.Start();
			if (!ExcelLegendLinkMetadata.TryWrite(legend, migrated, out string paramError))
			{
				tx.RollBack();
				error = paramError;
				return false;
			}
			tx.Commit();
			metadata = migrated;
			return true;
		}
		catch (Exception ex)
		{
			if (tx.HasStarted())
			{
				tx.RollBack();
			}
			error = ex.Message;
			return false;
		}
	}

	private static bool NeedsPdfMigration(Document document, ImageInstance image)
	{
		if (!ExcelLegendLinkService.TryGetLinkedImagePath(document, image, out string imagePath) || string.IsNullOrWhiteSpace(imagePath))
		{
			return false;
		}
		string extension = Path.GetExtension(imagePath);
		return !string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
	}

	private static string BuildExportCacheKey(string excelPath, string sheetName, string definedName)
	{
		return excelPath + "|" + sheetName + "|" + definedName;
	}

	private static string BuildScopedNameKey(string sheetName, string definedName)
	{
		return ExcelLegendLinkMetadata.BuildScopedDefinedName(sheetName, definedName);
	}
}
