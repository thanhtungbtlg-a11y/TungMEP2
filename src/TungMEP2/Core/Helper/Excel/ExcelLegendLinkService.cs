using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelLegendLinkService
{
	internal sealed record LinkedImageInfo(View Legend, ImageInstance Image, ExcelLegendLinkMetadata Metadata, string ExcelPath, string DefinedName);

	internal enum LinkedImagePathError
	{
		None,
		ImageTypeNotFound,
		ExternalReferenceMissing,
		ResolvedPathEmpty,
		ApiException
	}

	public sealed record DefinedNameChoice(string SheetName, string DefinedName)
	{
		public string ScopedDefinedName => ExcelLegendLinkMetadata.BuildScopedDefinedName(SheetName, DefinedName);

		public string DisplayName => ScopedDefinedName;
	}

	private static readonly XYZ FixedAnchorPoint = new XYZ(1.0, 1.0, 0.0);

	private const double AnchorTolerance = 1E-06;

	public static ExcelLegendLinkSummary RunSingle(Document document, ElementId activeLegendId, string excelPath, string selectedDefinedName)
	{
		return RunSingle(document, activeLegendId, excelPath, new DefinedNameChoice(string.Empty, selectedDefinedName));
	}

	public static ExcelLegendLinkSummary RunSingle(Document document, ElementId activeLegendId, string excelPath, DefinedNameChoice selectedDefinedNameChoice)
	{
		List<ExcelLegendLinkItemResult> items = new List<ExcelLegendLinkItemResult>();
		int updated = 0;
		int failed = 0;
		string selectedDefinedName = selectedDefinedNameChoice?.DefinedName ?? string.Empty;
		string selectedSheetName = selectedDefinedNameChoice?.SheetName ?? string.Empty;
		string selectedScopedDefinedName = selectedDefinedNameChoice?.ScopedDefinedName ?? string.Empty;
		if (document == null)
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, excelPath, string.Empty, ExcelLegendLinkAction.Failed, "Document is null.", IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		if (string.IsNullOrWhiteSpace(selectedDefinedName))
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, excelPath, string.Empty, ExcelLegendLinkAction.Failed, "Defined name is required.", IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		if (string.IsNullOrWhiteSpace(selectedSheetName))
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, "Sheet scope is required. Please reload and select a scoped defined name.", IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		if (!(document.GetElement(activeLegendId) is View { ViewType: ViewType.Legend } activeLegend))
		{
			items.Add(new ExcelLegendLinkItemResult(null, string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, "Active legend is invalid.", IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		if (!ExcelLegendLinkMetadata.EnsureParameters(document, out string parameterError))
		{
			items.Add(new ExcelLegendLinkItemResult(activeLegend.Id.Value, activeLegend.Name ?? string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, "Cannot ensure VP_Excel_* parameters: " + parameterError, IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		if (!OptionalFunctionalityUtils.IsPDFImportAvailable())
		{
			items.Add(new ExcelLegendLinkItemResult(activeLegend.Id.Value, activeLegend.Name ?? string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, "PDF import không khả dụng trong bản cài Revit hiện tại.", IsWarning: true));
			return BuildSummary(0, 0, updated, 0, 0, 0, failed + 1, items);
		}
		ExcelDefinedNameExportSummary exportSummary = ExcelDefinedNamePdfExporter.Export(excelPath, (IReadOnlyCollection<ExcelDefinedNameExportTarget>?)(object)new ExcelDefinedNameExportTarget[1]
		{
			new ExcelDefinedNameExportTarget(selectedSheetName, selectedDefinedName)
		});
		ExcelDefinedNameExportItemResult exportedItem = exportSummary.Items.FirstOrDefault((ExcelDefinedNameExportItemResult item) => string.Equals(item.DefinedName, selectedDefinedName, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(selectedSheetName) || string.Equals(item.SheetName, selectedSheetName, StringComparison.OrdinalIgnoreCase)) && item.IsExported && !string.IsNullOrWhiteSpace(item.OutputPath));
		if (exportedItem == null)
		{
			ExcelDefinedNameExportItemResult failedItem = exportSummary.Items.FirstOrDefault((ExcelDefinedNameExportItemResult item) => string.Equals(item.DefinedName, selectedDefinedName, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(selectedSheetName) || string.Equals(item.SheetName, selectedSheetName, StringComparison.OrdinalIgnoreCase)));
			items.Add(new ExcelLegendLinkItemResult(activeLegend.Id.Value, activeLegend.Name ?? string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, failedItem?.Message ?? "Cannot export selected defined name.", IsWarning: true));
			return BuildSummary(1, 0, 0, 0, 0, 0, 1, items);
		}
		using Transaction tx = new Transaction(document, "Link " + selectedDefinedName + " to active legend");
		try
		{
			tx.Start();
			ExcelLegendLinkMetadata metadata;
			string error;
			bool hadProvider = ExcelLegendLinkMetadata.TryRead(activeLegend, out metadata, out error);
			ImageInstance managedImage = FindManagedImageInLegend(activeLegend);
			if (managedImage != null)
			{
				if (!TryRefreshManagedImageInPlace(document, managedImage, exportedItem.OutputPath, out string refreshError))
				{
					throw new InvalidOperationException("Failed to refresh linked image: " + refreshError);
				}
				ImageInstance image = managedImage;
				updated++;
			}
			else
			{
				ImageInstance image = CreateLinkedImageInstance(document, activeLegend, exportedItem.OutputPath, FixedAnchorPoint);
				if (image == null)
				{
					throw new InvalidOperationException("Failed to create linked image instance.");
				}
			}
			long lastWriteTicks = (File.Exists(excelPath) ? File.GetLastWriteTimeUtc(excelPath).Ticks : DateTime.UtcNow.Ticks);
			if (!TryGetFileStamp(exportedItem.OutputPath, out long imageLastWriteTicks, out long imageFileSize, out string imageStampError))
			{
				throw new InvalidOperationException("Cannot read linked image file stamp: " + imageStampError);
			}
			ExcelLegendLinkMetadata metadata2 = ExcelLegendLinkMetadata.Create(excelPath, selectedSheetName, selectedDefinedName, lastWriteTicks, imageLastWriteTicks, imageFileSize);
			if (!ExcelLegendLinkMetadata.TryWrite(activeLegend, metadata2, out string nameError))
			{
				throw new InvalidOperationException("Cannot write metadata to VP_Excel_* parameters: " + nameError);
			}
			tx.Commit();
			items.Add(new ExcelLegendLinkItemResult(activeLegend.Id.Value, activeLegend.Name ?? string.Empty, excelPath, selectedDefinedName, (hadProvider || managedImage != null) ? ExcelLegendLinkAction.Updated : ExcelLegendLinkAction.Created, "Linked image " + Path.GetFileName(exportedItem.OutputPath) + " (" + selectedScopedDefinedName + ")"));
		}
		catch (Exception ex)
		{
			if (tx.HasStarted())
			{
				tx.RollBack();
			}
			failed++;
			items.Add(new ExcelLegendLinkItemResult(activeLegend.Id.Value, activeLegend.Name ?? string.Empty, excelPath, selectedDefinedName, ExcelLegendLinkAction.Failed, "LinkFailed: " + ex.Message, IsWarning: true));
		}
		return BuildSummary(1, (updated == 0 && failed == 0) ? 1 : 0, updated, 0, 0, 0, failed, items);
	}

	public static bool TryResolveTargetLegend(Document document, ElementId activeLegendId, string desiredLegendName, out ElementId targetLegendId, out string message)
	{
		targetLegendId = ElementId.InvalidElementId;
		message = string.Empty;
		if (document == null)
		{
			message = "Không tìm thấy tài liệu hiện tại.";
			return false;
		}
		if (document.GetElement(activeLegendId) is View { ViewType: ViewType.Legend } activeLegend)
		{
			targetLegendId = activeLegend.Id;
			return true;
		}
		View baseLegend = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().FirstOrDefault((View view) => view.ViewType == ViewType.Legend && !view.IsTemplate);
		if (baseLegend == null)
		{
			message = "Không tìm thấy Legend có sẵn để tạo mới. Vui lòng tạo ít nhất 1 Legend trước khi liên kết.";
			return false;
		}
		using Transaction tx = new Transaction(document, "Create legend for Excel link");
		try
		{
			tx.Start();
			ElementId createdLegendId = baseLegend.Duplicate(ViewDuplicateOption.Duplicate);
			if (!(document.GetElement(createdLegendId) is View { ViewType: ViewType.Legend } createdLegend))
			{
				throw new InvalidOperationException("Không thể tạo Legend mới.");
			}
			createdLegend.Name = BuildUniqueLegendName(document, desiredLegendName);
			createdLegend.Scale = 1;
			tx.Commit();
			targetLegendId = createdLegend.Id;
			return true;
		}
		catch (Exception ex)
		{
			if (tx.HasStarted())
			{
				tx.RollBack();
			}
			message = "Không thể tạo Legend mới: " + ex.Message;
			return false;
		}
	}

	public static bool TryResetActiveLegendLink(Document document, ElementId activeLegendId, out string message)
	{
		message = string.Empty;
		if (document == null)
		{
			message = "Không tìm thấy tài liệu hiện tại.";
			return false;
		}
		if (document.GetElement(activeLegendId) is View { ViewType: ViewType.Legend } activeLegend)
		{
			ExcelLegendLinkMetadata metadata;
			string error;
			bool hadMetadata = ExcelLegendLinkMetadata.TryRead(activeLegend, out metadata, out error);
			bool removedManagedImage = false;
			using Transaction tx = new Transaction(document, "Reset Excel Legend Link");
			try
			{
				tx.Start();
				ImageInstance managedImage = FindManagedImageInLegend(activeLegend);
				if (managedImage != null && TryGetLinkedImagePath(document, managedImage, out error))
				{
					document.Delete(managedImage.Id);
					removedManagedImage = true;
				}
				if (!ExcelLegendLinkMetadata.TryClear(activeLegend, out string clearError))
				{
					throw new InvalidOperationException("Không thể xóa metadata VP_Excel_*: " + clearError);
				}
				tx.Commit();
			}
			catch (Exception ex)
			{
				if (tx.HasStarted())
				{
					tx.RollBack();
				}
				message = "Làm mới thất bại: " + ex.Message;
				return false;
			}
			if (hadMetadata || removedManagedImage)
			{
				message = "Đã làm mới: đã xóa metadata VP_Excel_* và ảnh liên kết được quản lý (nếu có).";
			}
			else
			{
				message = "Không có dữ liệu liên kết Excel để làm mới trong Legend đang mở.";
			}
			return true;
		}
		message = "Làm mới yêu cầu active view là Legend.";
		return false;
	}

	public static IReadOnlyList<DefinedNameChoice> LoadDefinedNames(string excelPath)
	{
		List<DefinedNameChoice> choices = new List<DefinedNameChoice>();
		using ExcelInteropSession session = ExcelInteropSession.OpenReadOnly(excelPath);
		SheetScopedNameScanner.ForEachDefinedName(session.Workbook, delegate(Worksheet? worksheet, Name name, bool isWorkbookScope)
		{
			string sheetName = (isWorkbookScope ? "Workbook" : (worksheet?.Name ?? "Worksheet"));
			choices.Add(new DefinedNameChoice(sheetName, name.Name));
		});
		return choices.OrderBy<DefinedNameChoice, string>((DefinedNameChoice choice) => choice.DefinedName, StringComparer.OrdinalIgnoreCase).ThenBy<DefinedNameChoice, string>((DefinedNameChoice choice) => choice.SheetName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	internal static ImageInstance? FindManagedImageInLegend(View legend)
	{
		Document document = legend.Document;
		List<ImageInstance> images = new FilteredElementCollector(document, legend.Id).OfClass(typeof(ImageInstance)).Cast<ImageInstance>().ToList();
		if (images.Count == 0)
		{
			return null;
		}
		string imagePath;
		List<ImageInstance> linkedImages = images.Where((ImageInstance image) => TryGetLinkedImagePath(document, image, out imagePath) && !string.IsNullOrWhiteSpace(imagePath)).ToList();
		if (linkedImages.Count > 0)
		{
			ImageInstance linkedAtAnchor = linkedImages.FirstOrDefault(IsAtFixedAnchor);
			return linkedAtAnchor ?? linkedImages.First();
		}
		ImageInstance atAnchor = images.FirstOrDefault(IsAtFixedAnchor);
		return atAnchor ?? images.First();
	}

	internal static bool IsAtFixedAnchor(ImageInstance image)
	{
		if (!(image.Location is LocationPoint point))
		{
			return false;
		}
		return point.Point.DistanceTo(FixedAnchorPoint) <= 1E-06;
	}

	internal static XYZ GetFixedAnchorPoint()
	{
		return new XYZ(FixedAnchorPoint.X, FixedAnchorPoint.Y, FixedAnchorPoint.Z);
	}

	internal static ImageInstance CreateLinkedImageInstance(Document document, View legend, string imagePath, XYZ placementPoint)
	{
		ImageTypeOptions imageTypeOptions = CreateLinkedImageTypeOptions(imagePath);
		ImageType imageType = ImageType.Create(document, imageTypeOptions);
		ImagePlacementOptions placement = new ImagePlacementOptions(placementPoint, BoxPlacement.Center);
		return ImageInstance.Create(document, legend, imageType.Id, placement);
	}

	internal static bool TryRefreshManagedImageInPlace(Document document, ImageInstance image, string imagePath, out string error)
	{
		error = string.Empty;
		if (!(document.GetElement(image.GetTypeId()) is ImageType imageType))
		{
			error = "Cannot resolve image type from managed image.";
			return false;
		}
		if (TryReloadImageType(imageType, imagePath, out error))
		{
			return true;
		}
		try
		{
			ImageTypeOptions options = CreateLinkedImageTypeOptions(imagePath);
			ImageType replacementType = ImageType.Create(document, options);
			image.ChangeTypeId(replacementType.Id);
			return true;
		}
		catch (Exception ex)
		{
			error = (string.IsNullOrWhiteSpace(error) ? ex.Message : (error + "; Fallback failed: " + ex.Message));
			return false;
		}
	}

	internal static bool TryGetLinkedImagePath(Document document, ImageInstance image, out string imagePath)
	{
		LinkedImagePathError error;
		string detail;
		return TryGetLinkedImagePathDetailed(document, image, out imagePath, out error, out detail);
	}

	internal static bool TryGetLinkedImageFileStamp(Document document, ImageInstance image, out long imageLastWriteTicks, out long imageFileSize, out string error)
	{
		imageLastWriteTicks = 0L;
		imageFileSize = -1L;
		error = string.Empty;
		if (!TryGetLinkedImagePathDetailed(document, image, out string imagePath, out LinkedImagePathError _, out string detail) || string.IsNullOrWhiteSpace(imagePath))
		{
			error = (string.IsNullOrWhiteSpace(detail) ? "Cannot resolve linked image path." : detail);
			return false;
		}
		return TryGetFileStamp(imagePath, out imageLastWriteTicks, out imageFileSize, out error);
	}

	internal static bool TryGetFileStamp(string filePath, out long lastWriteTicks, out long fileSize, out string error)
	{
		lastWriteTicks = 0L;
		fileSize = -1L;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(filePath))
		{
			error = "File path is empty.";
			return false;
		}
		if (!File.Exists(filePath))
		{
			error = "File does not exist: " + filePath;
			return false;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(filePath);
			lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
			fileSize = fileInfo.Length;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	internal static bool TryGetLinkedImagePathDetailed(Document document, ImageInstance image, out string imagePath, out LinkedImagePathError error, out string detail)
	{
		imagePath = string.Empty;
		error = LinkedImagePathError.None;
		detail = string.Empty;
		if (!(document.GetElement(image.GetTypeId()) is ImageType imageType))
		{
			error = LinkedImagePathError.ImageTypeNotFound;
			detail = "Cannot resolve ImageType from managed image.";
			return false;
		}
		try
		{
			ExternalFileReference externalFileReference = imageType.GetExternalFileReference();
			if (externalFileReference != null)
			{
				ModelPath modelPath = externalFileReference.GetAbsolutePath();
				imagePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
				if (!string.IsNullOrWhiteSpace(imagePath))
				{
					return true;
				}
				error = LinkedImagePathError.ResolvedPathEmpty;
				detail = "External reference resolved to empty path.";
			}
			else
			{
				error = LinkedImagePathError.ExternalReferenceMissing;
				detail = "ImageType has no ExternalFileReference.";
			}
		}
		catch (Exception ex)
		{
			error = LinkedImagePathError.ApiException;
			detail = ex.GetType().Name + ": " + ex.Message;
		}
		if (TryResolveLinkedImagePathViaRuntime(imageType, out imagePath, out string runtimeDetail))
		{
			detail = (string.IsNullOrWhiteSpace(detail) ? runtimeDetail : (detail + " | Fallback: " + runtimeDetail));
			return true;
		}
		if (!string.IsNullOrWhiteSpace(runtimeDetail))
		{
			detail = (string.IsNullOrWhiteSpace(detail) ? runtimeDetail : (detail + " | Fallback: " + runtimeDetail));
		}
		return false;
	}

	private static bool TryResolveLinkedImagePathViaRuntime(ImageType imageType, out string imagePath, out string detail)
	{
		imagePath = string.Empty;
		detail = string.Empty;
		Type runtimeType = imageType.GetType();
		string[] propertyCandidates = new string[6] { "Path", "SourcePath", "FilePath", "ImagePath", "Source", "PathName" };
		string[] array = propertyCandidates;
		foreach (string propertyName in array)
		{
			PropertyInfo property = runtimeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(property == null))
			{
				object rawValue;
				try
				{
					rawValue = property.GetValue(imageType, null);
				}
				catch (Exception ex)
				{
					detail = "Runtime property '" + propertyName + "' failed: " + ex.Message;
					continue;
				}
				if (TryNormalizePathCandidate(rawValue, out imagePath))
				{
					detail = "Resolved from runtime property '" + propertyName + "'.";
					return true;
				}
			}
		}
		string[] methodCandidates = new string[3] { "GetPath", "GetSourcePath", "GetFilePath" };
		string[] array2 = methodCandidates;
		foreach (string methodName in array2)
		{
			MethodInfo method = runtimeType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (!(method == null))
			{
				object result;
				try
				{
					result = method.Invoke(imageType, null);
				}
				catch (Exception ex2)
				{
					detail = "Runtime method '" + methodName + "' failed: " + ex2.Message;
					continue;
				}
				if (TryNormalizePathCandidate(result, out imagePath))
				{
					detail = "Resolved from runtime method '" + methodName + "'.";
					return true;
				}
			}
		}
		if (string.IsNullOrWhiteSpace(detail))
		{
			detail = "No runtime path property/method returned a usable path.";
		}
		return false;
	}

	private static bool TryNormalizePathCandidate(object? candidate, out string imagePath)
	{
		imagePath = string.Empty;
		if (candidate == null)
		{
			return false;
		}
		string candidateText = ((!(candidate is string value)) ? ((!(candidate is ModelPath modelPath)) ? (candidate.ToString() ?? string.Empty) : ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath)) : value);
		candidateText = candidateText.Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(candidateText))
		{
			return false;
		}
		if (Uri.TryCreate(candidateText, UriKind.Absolute, out var uri) && uri.IsFile)
		{
			candidateText = uri.LocalPath;
		}
		imagePath = candidateText;
		return true;
	}

	private static bool TryReloadImageType(ImageType imageType, string imagePath, out string error)
	{
		error = string.Empty;
		Type imageTypeRuntime = imageType.GetType();
		ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(imagePath);
		ImageTypeOptions options = CreateLinkedImageTypeOptions(imagePath);
		if (TryInvokeReload(imageType, imageTypeRuntime, "ReloadFrom", new Type[1] { typeof(ImageTypeOptions) }, new object[1] { options }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "LoadFrom", new Type[1] { typeof(ImageTypeOptions) }, new object[1] { options }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "ReloadFrom", new Type[1] { typeof(ModelPath) }, new object[1] { modelPath }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "LoadFrom", new Type[1] { typeof(ModelPath) }, new object[1] { modelPath }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "ReloadFrom", new Type[1] { typeof(string) }, new object[1] { imagePath }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "LoadFrom", new Type[1] { typeof(string) }, new object[1] { imagePath }, out error))
		{
			return true;
		}
		if (TryInvokeReload(imageType, imageTypeRuntime, "Reload", Type.EmptyTypes, Array.Empty<object>(), out error))
		{
			return true;
		}
		if (string.IsNullOrWhiteSpace(error))
		{
			error = "ImageType reload API is unavailable in this Revit version.";
		}
		return false;
	}

	private static bool TryInvokeReload(ImageType imageType, Type imageTypeRuntime, string methodName, Type[] parameterTypes, object[] args, out string error)
	{
		error = string.Empty;
		MethodInfo method = imageTypeRuntime.GetMethod(methodName, parameterTypes);
		if (method == null)
		{
			return false;
		}
		try
		{
			if (method.Invoke(imageType, args) is bool success)
			{
				if (!success)
				{
					error = methodName + " returned false.";
				}
				return success;
			}
			return true;
		}
		catch (Exception ex)
		{
			Exception root = ((ex is TargetInvocationException { InnerException: not null } tie) ? tie.InnerException : ex);
			error = methodName + " failed: " + root.Message;
			return false;
		}
	}

	private static ImageTypeOptions CreateLinkedImageTypeOptions(string path)
	{
		return new ImageTypeOptions(path, useRelativePath: false, ImageTypeSource.Link)
		{
			Resolution = 600.0,
			PageNumber = 1
		};
	}

	private static string BuildUniqueLegendName(Document document, string desiredLegendName)
	{
		string baseName = (string.IsNullOrWhiteSpace(desiredLegendName) ? "Legend" : desiredLegendName.Trim());
		HashSet<string> existingNames = new HashSet<string>(from View view in new FilteredElementCollector(document).OfClass(typeof(View))
			where view.ViewType == ViewType.Legend && !view.IsTemplate
			select view.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
		if (!existingNames.Contains(baseName))
		{
			return baseName;
		}
		for (int i = 2; i <= 999; i++)
		{
			string candidate = $"{baseName} ({i})";
			if (!existingNames.Contains(candidate))
			{
				return candidate;
			}
		}
		return baseName + " (999+)";
	}

	private static ExcelLegendLinkSummary BuildSummary(int scanned, int created, int updated, int unchanged, int staleFound, int orphaned, int failed, IReadOnlyList<ExcelLegendLinkItemResult> items)
	{
		int parseError = items.Count((ExcelLegendLinkItemResult item) => item.Action == ExcelLegendLinkAction.Skipped && item.Message.IndexOf("parse", StringComparison.OrdinalIgnoreCase) >= 0);
		return new ExcelLegendLinkSummary(scanned, created, updated, unchanged, staleFound, orphaned, parseError, failed, items);
	}
}
