using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelLegendCheckerService
{
	public static ExcelLegendCheckerScanSummary Scan(Document document)
	{
		List<ExcelLegendCheckerRow> rows = new List<ExcelLegendCheckerRow>();
		int fresh = 0;
		int stale = 0;
		int fileError = 0;
		foreach (View legend in from View view in new FilteredElementCollector(document).OfClass(typeof(View))
			where view.ViewType == ViewType.Legend && !view.IsTemplate
			select view)
		{
			string legendName = legend.Name ?? string.Empty;
			if (ExcelLegendLinkMetadata.TryRead(legend, out ExcelLegendLinkMetadata metadata, out string _) && !(metadata == null))
			{
				string message = string.Empty;
				ExcelLegendCheckerStatus status = ResolveStatus(document, legend, metadata, out message);
				switch (status)
				{
				case ExcelLegendCheckerStatus.Fresh:
					fresh++;
					break;
				case ExcelLegendCheckerStatus.Stale:
					stale++;
					break;
				default:
					fileError++;
					break;
				}
				rows.Add(new ExcelLegendCheckerRow(legend.Id.Value, legendName, metadata.WorkbookPath, metadata.ScopedDefinedName, status, message));
			}
		}
		return new ExcelLegendCheckerScanSummary(rows.Count, fresh, stale, fileError, rows.OrderBy<ExcelLegendCheckerRow, string>((ExcelLegendCheckerRow row) => row.LegendName, StringComparer.OrdinalIgnoreCase).ToList());
	}

	public static ExcelLegendLinkSummary Update(Document document, IReadOnlyList<ExcelLegendCheckerUpdateRequest> requests)
	{
		List<ExcelLegendLinkItemResult> items = new List<ExcelLegendLinkItemResult>();
		int scanned = 0;
		int created = 0;
		int updated = 0;
		int unchanged = 0;
		int staleFound = 0;
		int orphaned = 0;
		int parseError = 0;
		int failed = 0;
		foreach (ExcelLegendCheckerUpdateRequest request in requests)
		{
			if (!ExcelLegendLinkMetadata.TrySplitScopedDefinedName(request.ScopedDefinedName, out string sheetName, out string definedName))
			{
				failed++;
				items.Add(new ExcelLegendLinkItemResult(request.LegendId, string.Empty, request.ExcelPath, request.ScopedDefinedName, ExcelLegendLinkAction.Failed, "Tên vùng theo phạm vi không hợp lệ.", IsWarning: true));
				continue;
			}
			ExcelLegendLinkSummary rowSummary = ExcelLegendLinkService.RunSingle(document, new ElementId(request.LegendId), request.ExcelPath, new ExcelLegendLinkService.DefinedNameChoice(sheetName, definedName));
			scanned += rowSummary.Scanned;
			created += rowSummary.Created;
			updated += rowSummary.Updated;
			unchanged += rowSummary.Unchanged;
			staleFound += rowSummary.StaleFound;
			orphaned += rowSummary.Orphaned;
			parseError += rowSummary.ParseError;
			failed += rowSummary.Failed;
			items.AddRange(rowSummary.Items);
		}
		return new ExcelLegendLinkSummary(scanned, created, updated, unchanged, staleFound, orphaned, parseError, failed, items);
	}

	private static ExcelLegendCheckerStatus ResolveStatus(Document document, View legend, ExcelLegendLinkMetadata metadata, out string message)
	{
		message = string.Empty;
		if (string.IsNullOrWhiteSpace(metadata.WorkbookPath) || !File.Exists(metadata.WorkbookPath))
		{
			message = "Lỗi tệp Excel: Đường dẫn không tồn tại.";
			return ExcelLegendCheckerStatus.FileError;
		}
		if (string.IsNullOrWhiteSpace(metadata.SheetName))
		{
			message = "Lỗi tệp: Siêu dữ liệu thiếu tên sheet, vui lòng liên kết lại từ cửa sổ Link Sync.";
			return ExcelLegendCheckerStatus.FileError;
		}
		if (!OptionalFunctionalityUtils.IsPDFImportAvailable())
		{
			message = "Lỗi hệ thống: PDF import không khả dụng trong bản cài Revit hiện tại.";
			return ExcelLegendCheckerStatus.FileError;
		}
		ImageInstance managedImage = ExcelLegendLinkService.FindManagedImageInLegend(legend);
		if (managedImage == null)
		{
			message = "Lỗi tệp: Không tìm thấy ảnh liên kết được quản lý trong khung chú giải.";
			return ExcelLegendCheckerStatus.FileError;
		}
		if (!TryValidateLinkedImageFile(document, managedImage, out string imageError))
		{
			message = imageError;
			return ExcelLegendCheckerStatus.FileError;
		}
		long currentLastWriteTicks = File.GetLastWriteTimeUtc(metadata.WorkbookPath).Ticks;
		if (!ExcelLegendLinkService.TryGetLinkedImageFileStamp(document, managedImage, out long currentImageLastWriteTicks, out long currentImageFileSize, out string imageStampError))
		{
			message = "Lỗi tệp ảnh liên kết: Không đọc được thông tin thay đổi tệp ảnh. Chi tiết: " + imageStampError;
			return ExcelLegendCheckerStatus.FileError;
		}
		if (ExcelLegendLinkMetadata.HasSourceChanged(metadata, currentLastWriteTicks, currentImageLastWriteTicks, currentImageFileSize) || NeedsPdfMigration(document, managedImage))
		{
			message = "Cũ: Tệp Excel hoặc ảnh liên kết đã thay đổi, cần cập nhật.";
			return ExcelLegendCheckerStatus.Stale;
		}
		message = string.Empty;
		return ExcelLegendCheckerStatus.Fresh;
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

	private static bool TryValidateLinkedImageFile(Document document, ImageInstance image, out string error)
	{
		error = string.Empty;
		if (!ExcelLegendLinkService.TryGetLinkedImagePathDetailed(document, image, out string imagePath, out ExcelLegendLinkService.LinkedImagePathError _, out string pathDetail) || string.IsNullOrWhiteSpace(imagePath))
		{
			error = (string.IsNullOrWhiteSpace(pathDetail) ? "Lỗi tệp ảnh liên kết: Không đọc được đường dẫn ảnh." : ("Lỗi tệp ảnh liên kết: Không đọc được đường dẫn ảnh. Chi tiết: " + pathDetail));
			return false;
		}
		if (!File.Exists(imagePath))
		{
			error = "Lỗi tệp ảnh liên kết: Không tồn tại.";
			return false;
		}
		return true;
	}
}
