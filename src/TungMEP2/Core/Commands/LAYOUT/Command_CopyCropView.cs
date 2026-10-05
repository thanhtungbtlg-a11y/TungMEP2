using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_CopyCropView : SecuredExternalCommand
{
	private const string DialogTitle = "Copy Crop View";

	protected override void ExecuteCore()
	{
		try
		{
			int validOpenViewCount = UI_CopyCropViewWindow.CountEligibleOpenViews(base.UiDocument);
			if (validOpenViewCount < 2)
			{
				TaskDialog.Show("Copy Crop View", "Không tìm thấy đủ ít nhất 2 view hợp lệ đang mở.\n\nChỉ hỗ trợ view loại Plan/Section/Elevation (không phải template, không phải sheet).\nVui lòng mở thêm view hợp lệ rồi chạy lại lệnh.");
				return;
			}
			UI_CopyCropViewWindow window = new UI_CopyCropViewWindow(base.UiDocument);
			if (window.ShowDialog() != true || window.SourceViewId == null || window.TargetViewId == null)
			{
				return;
			}
			if (window.SourceViewId == window.TargetViewId)
			{
				TaskDialog.Show("Copy Crop View", "Source view và target view phải khác nhau.");
				return;
			}
			Element sourceElement = base.Document.GetElement(window.SourceViewId);
			Element targetElement = base.Document.GetElement(window.TargetViewId);
			if (!(sourceElement is View sourceView) || !(targetElement is View targetView))
			{
				TaskDialog.Show("Copy Crop View", "Không thể đọc source/target view. Vui lòng chọn lại.");
				return;
			}
			if (!UI_CopyCropViewWindow.IsEligibleView(sourceView) || !UI_CopyCropViewWindow.IsEligibleView(targetView))
			{
				TaskDialog.Show("Copy Crop View", "Source/target view không thuộc phạm vi hỗ trợ (Plan/Section/Elevation).");
				return;
			}
			if (sourceView.ViewType != targetView.ViewType)
			{
				TaskDialog.Show("Copy Crop View", $"Không thể copy giữa 2 loại view khác nhau.\nSource: {sourceView.ViewType}\nTarget: {targetView.ViewType}");
				return;
			}
			int okCount = 0;
			int failCount = 0;
			int skipCount = 0;
			List<string> report = new List<string>();
			using Transaction transaction = new Transaction(base.Document, "Copy Crop View");
			transaction.Start();
			try
			{
				try
				{
					targetView.CropBoxActive = sourceView.CropBoxActive;
					okCount++;
					report.Add($"OK: Crop View = {sourceView.CropBoxActive}");
				}
				catch (Exception ex)
				{
					failCount++;
					report.Add("FAIL: Crop View - " + ex.Message);
				}
				try
				{
					targetView.CropBoxVisible = sourceView.CropBoxVisible;
					okCount++;
					report.Add($"OK: Crop Region Visible = {sourceView.CropBoxVisible}");
				}
				catch (Exception ex2)
				{
					failCount++;
					report.Add("FAIL: Crop Region Visible - " + ex2.Message);
				}
				Parameter sourceAnnotationCrop = ((Element)sourceView).get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
				Parameter targetAnnotationCrop = ((Element)targetView).get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
				if (sourceAnnotationCrop == null || targetAnnotationCrop == null)
				{
					skipCount++;
					report.Add("SKIP: Annotation Crop - Không tìm thấy parameter trên source hoặc target.");
				}
				else if (targetAnnotationCrop.IsReadOnly)
				{
					skipCount++;
					report.Add("SKIP: Annotation Crop - Parameter trên target là read-only.");
				}
				else
				{
					try
					{
						int sourceValue = sourceAnnotationCrop.AsInteger();
						targetAnnotationCrop.Set(sourceValue);
						okCount++;
						report.Add($"OK: Annotation Crop = {sourceValue != 0}");
					}
					catch (Exception ex3)
					{
						failCount++;
						report.Add("FAIL: Annotation Crop - " + ex3.Message);
					}
				}
				try
				{
					ViewCropRegionShapeManager sourceShapeManager = sourceView.GetCropRegionShapeManager();
					ViewCropRegionShapeManager targetShapeManager = targetView.GetCropRegionShapeManager();
					if (sourceShapeManager == null || targetShapeManager == null)
					{
						skipCount++;
						report.Add("SKIP: Crop Shape - View không hỗ trợ crop region shape manager.");
					}
					else
					{
						IList<CurveLoop> cropShapes = sourceShapeManager.GetCropShape();
						if (cropShapes == null || cropShapes.Count == 0)
						{
							skipCount++;
							report.Add("SKIP: Crop Shape - Source view không có crop shape hợp lệ.");
						}
						else
						{
							targetShapeManager.SetCropShape(cropShapes[0]);
							okCount++;
							report.Add("OK: Crop Shape");
						}
					}
				}
				catch (Exception ex4)
				{
					failCount++;
					report.Add("FAIL: Crop Shape - " + ex4.Message);
				}
				if (okCount > 0)
				{
					transaction.Commit();
				}
				else
				{
					transaction.RollBack();
				}
			}
			catch
			{
				if (transaction.GetStatus() == TransactionStatus.Started)
				{
					transaction.RollBack();
				}
				throw;
			}
			if (skipCount > 0 || failCount > 0)
			{
				TaskDialog.Show("Copy Crop View", "Source: " + sourceView.Name + "\nTarget: " + targetView.Name + "\n\n" + $"OK: {okCount}\n" + $"SKIP: {skipCount}\n" + $"FAIL: {failCount}\n\n" + string.Join("\n", report));
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex6)
		{
			TaskDialog.Show("Copy Crop View", "Đã xảy ra lỗi khi copy crop view.\n\nLý do: " + ex6.Message);
		}
	}
}
