using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets.AlignViewports;

public static class ViewportAligner
{
	private sealed class SheetViewportContext
	{
		public ViewSheet Sheet { get; }

		public FamilyInstance? TitleBlock { get; set; }

		public List<Viewport> PlanViewports { get; } = new List<Viewport>();

		public Viewport? LegendViewport { get; set; }

		public SheetViewportContext(ViewSheet sheet)
		{
			Sheet = sheet;
		}
	}

	public static AlignViewportsReport Align(Document document, ViewSheet mainSheet, IList<ViewSheet> otherSheets, AlignViewportsOptions options)
	{
		AlignViewportsReport report = new AlignViewportsReport();
		SheetViewportContext mainContext = CreateContext(document, mainSheet, report, isMainSheet: true);
		if (mainContext == null)
		{
			report.SkippedSheetCount += otherSheets.Count;
			return report;
		}
		if (!CanUseMainAsReference(document, mainContext, options, report))
		{
			report.SkippedSheetCount += otherSheets.Count;
			return report;
		}
		TransactionGroup group = new TransactionGroup(document, "Align Viewports");
		group.Start();
		foreach (ViewSheet otherSheet in otherSheets)
		{
			Transaction transaction = new Transaction(document, "Align Viewports " + otherSheet.SheetNumber);
			try
			{
				transaction.Start();
				SheetViewportContext otherContext = CreateContext(document, otherSheet, report, isMainSheet: false);
				if (otherContext == null)
				{
					report.SkippedSheetCount++;
					transaction.RollBack();
					continue;
				}
				if (otherContext.PlanViewports.Count > 1 && !options.OverlapMultipleViewPlans)
				{
					report.SkippedSheetCount++;
					report.Messages.Add("SKIP: " + otherSheet.SheetNumber + " has multiple plan viewports while overlap option is off.");
					transaction.RollBack();
					continue;
				}
				if (!HaveSameViewportScale(document, otherContext.PlanViewports))
				{
					report.SkippedSheetCount++;
					report.Messages.Add("SKIP: " + otherSheet.SheetNumber + " has multiple plan viewports with different scale.");
					transaction.RollBack();
					continue;
				}
				if (otherContext.PlanViewports.Count == 0)
				{
					report.SkippedSheetCount++;
					report.Messages.Add("SKIP: " + otherSheet.SheetNumber + " has no view plan viewport.");
					transaction.RollBack();
					continue;
				}
				if (options.ApplySameTitleBlockType)
				{
					ApplyTitleBlockType(mainContext.TitleBlock, otherContext.TitleBlock);
				}
				Viewport mainPlanViewport = mainContext.PlanViewports[0];
				View mainPlanView = (View)document.GetElement(mainPlanViewport.ViewId);
				XYZ mainAnchor = GetSheetAnchor(mainContext.TitleBlock);
				XYZ otherAnchor = GetSheetAnchor(otherContext.TitleBlock);
				foreach (Viewport otherPlanViewport in otherContext.PlanViewports)
				{
					View otherPlanView = (View)document.GetElement(otherPlanViewport.ViewId);
					if (options.ApplySameCropScopeBox)
					{
						ApplyCropAndScopeFromMain(mainPlanView, otherPlanView);
					}
					XYZ targetCenter = GetTargetCenter(mainPlanViewport.GetBoxCenter(), mainAnchor, otherAnchor);
					TrySetViewportCenter(otherPlanViewport, targetCenter, report, otherSheet.SheetNumber, "plan viewport");
					if (options.AlignViewportTitlesXY)
					{
						TryCopyViewportLabelLineLength(mainPlanViewport, otherPlanViewport);
						TryAlignViewportTitleXY(mainPlanViewport, otherPlanViewport, mainAnchor, otherAnchor, report, otherSheet.SheetNumber, "plan viewport title");
					}
				}
				if (options.AlignLegendIfSame)
				{
					AlignLegendIfSame(document, mainContext, otherContext, options, report);
				}
				report.AlignedSheetCount++;
				report.Messages.Add("OK: aligned " + otherSheet.SheetNumber);
				transaction.Commit();
			}
			catch (Exception ex)
			{
				if (transaction.HasStarted())
				{
					transaction.RollBack();
				}
				report.SkippedSheetCount++;
				report.Messages.Add("FAIL: " + otherSheet.SheetNumber + " - " + ex.Message);
			}
		}
		group.Assimilate();
		return report;
	}

	private static SheetViewportContext? CreateContext(Document document, ViewSheet sheet, AlignViewportsReport report, bool isMainSheet)
	{
		SheetViewportContext context = new SheetViewportContext(sheet)
		{
			TitleBlock = GetPrimaryTitleBlock(document, sheet)
		};
		foreach (ElementId viewportId in sheet.GetAllViewports())
		{
			if (document.GetElement(viewportId) is Viewport viewport && document.GetElement(viewport.ViewId) is View view)
			{
				if (view is ViewPlan)
				{
					context.PlanViewports.Add(viewport);
				}
				else if (view.ViewType == ViewType.Legend && context.LegendViewport == null)
				{
					context.LegendViewport = viewport;
				}
			}
		}
		if (context.PlanViewports.Count == 0 && isMainSheet)
		{
			report.Messages.Add("SKIP: main sheet " + sheet.SheetNumber + " has no view plan viewport.");
			return null;
		}
		return context;
	}

	private static bool CanUseMainAsReference(Document document, SheetViewportContext mainContext, AlignViewportsOptions options, AlignViewportsReport report)
	{
		if (mainContext.PlanViewports.Count == 0)
		{
			report.Messages.Add("SKIP: main sheet " + mainContext.Sheet.SheetNumber + " has no view plan viewport.");
			return false;
		}
		if (mainContext.PlanViewports.Count == 1)
		{
			return true;
		}
		if (!options.OverlapMultipleViewPlans)
		{
			report.Messages.Add("SKIP: main sheet " + mainContext.Sheet.SheetNumber + " has multiple plan viewports and overlap option is off.");
			return false;
		}
		if (!HaveSameViewportScale(document, mainContext.PlanViewports))
		{
			report.Messages.Add("SKIP: main sheet " + mainContext.Sheet.SheetNumber + " has multiple plan viewports with different scale.");
			return false;
		}
		List<string> centers = mainContext.PlanViewports.Select((Viewport viewport) => ToComparablePoint(viewport.GetBoxCenter())).Distinct<string>(StringComparer.Ordinal).ToList();
		if (centers.Count > 1)
		{
			report.Messages.Add("SKIP: main sheet " + mainContext.Sheet.SheetNumber + " has multiple plan viewports that are not overlapped.");
			return false;
		}
		return true;
	}

	private static bool HaveSameViewportScale(Document document, IList<Viewport> viewports)
	{
		if (viewports.Count <= 1)
		{
			return true;
		}
		string firstScale = null;
		foreach (Viewport viewport in viewports)
		{
			if (document.GetElement(viewport.ViewId) is View { Scale: var scale })
			{
				string currentScale = scale.ToString();
				if (firstScale == null)
				{
					firstScale = currentScale;
				}
				else if (!string.Equals(firstScale, currentScale, StringComparison.Ordinal))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static string ToComparablePoint(XYZ point)
	{
		return $"{Math.Round(point.X, 8)},{Math.Round(point.Y, 8)}";
	}

	private static FamilyInstance? GetPrimaryTitleBlock(Document document, ViewSheet sheet)
	{
		return (from FamilyInstance instance in new FilteredElementCollector(document, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
			orderby instance.Id.Value
			select instance).FirstOrDefault();
	}

	private static XYZ GetSheetAnchor(FamilyInstance? titleBlock)
	{
		if (!(titleBlock?.Location is LocationPoint { Point: var point }))
		{
			return XYZ.Zero;
		}
		return point;
	}

	private static XYZ GetTargetCenter(XYZ mainCenter, XYZ mainAnchor, XYZ otherAnchor)
	{
		XYZ delta = mainCenter - mainAnchor;
		return otherAnchor + delta;
	}

	private static void TrySetViewportCenter(Viewport viewport, XYZ target, AlignViewportsReport report, string sheetNumber, string label)
	{
		try
		{
			viewport.SetBoxCenter(target);
		}
		catch (Exception ex)
		{
			report.Messages.Add("WARN: could not align " + label + " on " + sheetNumber + ": " + ex.Message);
		}
	}

	private static void ApplyTitleBlockType(FamilyInstance? mainTitleBlock, FamilyInstance? otherTitleBlock)
	{
		if (mainTitleBlock != null && otherTitleBlock != null)
		{
			ElementId mainTypeId = mainTitleBlock.GetTypeId();
			if (mainTypeId != ElementId.InvalidElementId && otherTitleBlock.GetTypeId() != mainTypeId)
			{
				otherTitleBlock.ChangeTypeId(mainTypeId);
			}
		}
	}

	private static void ApplyCropAndScopeFromMain(View mainView, View otherView)
	{
		Parameter otherCrop = ((Element)otherView).get_Parameter(BuiltInParameter.VIEWER_CROP_REGION);
		if (otherCrop != null && !otherCrop.IsReadOnly && otherCrop.AsInteger() == 0)
		{
			otherCrop.Set(1);
		}
		Parameter mainScopeParameter = ((Element)mainView).get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
		Parameter otherScopeParameter = ((Element)otherView).get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
		ElementId mainScopeId = mainScopeParameter?.AsElementId() ?? ElementId.InvalidElementId;
		if (otherScopeParameter != null && !otherScopeParameter.IsReadOnly)
		{
			otherScopeParameter.Set(mainScopeId);
		}
		if (!(mainScopeId != ElementId.InvalidElementId))
		{
			IList<CurveLoop> mainShapes = mainView.GetCropRegionShapeManager().GetCropShape();
			if (mainShapes.Count != 0)
			{
				otherView.GetCropRegionShapeManager().SetCropShape(mainShapes[0]);
			}
		}
	}

	private static void AlignLegendIfSame(Document document, SheetViewportContext mainContext, SheetViewportContext otherContext, AlignViewportsOptions options, AlignViewportsReport report)
	{
		if (mainContext.LegendViewport == null || otherContext.LegendViewport == null || !(document.GetElement(mainContext.LegendViewport.ViewId) is View mainLegend) || !(document.GetElement(otherContext.LegendViewport.ViewId) is View otherLegend))
		{
			return;
		}
		if (!(mainLegend.Id == otherLegend.Id) && !string.Equals(mainLegend.Name, otherLegend.Name, StringComparison.OrdinalIgnoreCase))
		{
			report.Messages.Add("WARN: legend on " + otherContext.Sheet.SheetNumber + " does not match main sheet.");
			return;
		}
		XYZ mainAnchor = GetSheetAnchor(mainContext.TitleBlock);
		XYZ otherAnchor = GetSheetAnchor(otherContext.TitleBlock);
		XYZ targetCenter = GetTargetCenter(mainContext.LegendViewport.GetBoxCenter(), mainAnchor, otherAnchor);
		TrySetViewportCenter(otherContext.LegendViewport, targetCenter, report, otherContext.Sheet.SheetNumber, "legend viewport");
		if (options.AlignViewportTitlesXY)
		{
			TryCopyViewportLabelLineLength(mainContext.LegendViewport, otherContext.LegendViewport);
			TryAlignViewportTitleXY(mainContext.LegendViewport, otherContext.LegendViewport, mainAnchor, otherAnchor, report, otherContext.Sheet.SheetNumber, "legend viewport title");
		}
	}

	private static void TryAlignViewportTitleXY(Viewport sourceViewport, Viewport targetViewport, XYZ sourceSheetAnchor, XYZ targetSheetAnchor, AlignViewportsReport report, string sheetNumber, string label)
	{
		try
		{
			Outline sourceLabelOutline = sourceViewport.GetLabelOutline();
			Outline targetLabelOutline = targetViewport.GetLabelOutline();
			XYZ sourceLabelPoint = sourceLabelOutline.MinimumPoint;
			XYZ targetLabelPoint = targetLabelOutline.MinimumPoint;
			XYZ desiredLabelPoint = targetSheetAnchor + (sourceLabelPoint - sourceSheetAnchor);
			XYZ delta = desiredLabelPoint - targetLabelPoint;
			if (!(delta.GetLength() < 1E-09) && !TryApplyViewportLabelOffset(targetViewport, delta))
			{
				report.Messages.Add("WARN: could not align " + label + " on " + sheetNumber + " because viewport title offset API is unavailable.");
			}
		}
		catch (Exception ex)
		{
			report.Messages.Add("WARN: could not align " + label + " on " + sheetNumber + ": " + ex.Message);
		}
	}

	private static bool TryApplyViewportLabelOffset(Viewport targetViewport, XYZ delta)
	{
		bool wasPinned = targetViewport.Pinned;
		if (wasPinned)
		{
			targetViewport.Pinned = false;
		}
		try
		{
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
				return true;
			}
			MethodInfo setLabelOffsetMethod = targetType.GetMethod("SetLabelOffset", new Type[1] { typeof(XYZ) });
			if (setLabelOffsetMethod != null)
			{
				setLabelOffsetMethod.Invoke(targetViewport, new object[1] { delta });
				return true;
			}
			return false;
		}
		finally
		{
			if (wasPinned)
			{
				targetViewport.Pinned = true;
			}
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
