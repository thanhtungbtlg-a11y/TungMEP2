using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.FF;

[Transaction(TransactionMode.Manual)]
public class Command_AutoTagPipe : SecuredExternalCommand
{
	private const string DialogTitle = "AutoTagPipe";

	private const double DefaultLengthMinMillimeters = 1000.0;

	private const double DefaultMaxDnMillimeters = 100.0;

	private const double MaxSlopeRatio = 0.45;

	private const double VerticalTolerance = 0.01;

	private const double Epsilon = 1E-09;

	private const double TagGapPaperMillimeters = 2.0;

	private const int MinimumPlacementRings = 32;

	private const int MaximumPlacementRings = 512;

	protected override void ExecuteCore()
	{
		try
		{
			ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
			if (selectedIds.Count == 0)
			{
				TaskDialog.Show("AutoTagPipe", "Hãy chọn pipe trước khi chạy lệnh.");
				return;
			}
			List<ElementId> selectedPipeIds = (from pipe2 in selectedIds.Select(base.Document.GetElement).OfType<Pipe>()
				select pipe2.Id).Distinct().ToList();
			if (selectedPipeIds.Count == 0)
			{
				TaskDialog.Show("AutoTagPipe", "Selection hiện tại không có pipe hợp lệ.");
				return;
			}
			IndependentTag sampleTag = selectedIds.Select(base.Document.GetElement).OfType<IndependentTag>().FirstOrDefault(IsPipeTag);
			List<AutoTagPipeTagTypeItem> tagTypeItems = CollectPipeTagTypes();
			if (tagTypeItems.Count == 0)
			{
				TaskDialog.Show("AutoTagPipe", "Không tìm thấy Pipe Tag Type trong model.");
				return;
			}
			ElementId preferredTagTypeId = ResolvePreferredTagTypeId(sampleTag, tagTypeItems);
			UI_AutoTagPipeWindow window = new UI_AutoTagPipeWindow(tagTypeItems, preferredTagTypeId, 1000.0, 100.0);
			if (window.ShowDialog() != true)
			{
				return;
			}
			View activeView = base.Document.ActiveView;
			double minLengthInternal = UnitUtils.ConvertToInternalUnits(window.SelectedLengthMinMillimeters, UnitTypeId.Millimeters);
			double maxDnInternal = UnitUtils.ConvertToInternalUnits(window.SelectedMaxDnMillimeters, UnitTypeId.Millimeters);
			double tagGapInternal = PaperMillimetersToInternal(TagGapPaperMillimeters, activeView);
			List<AutoTagPipeRectangle> occupiedTagBounds = CollectExistingTagBounds(base.Document, activeView);
			int taggedCount = 0;
			int movedToAvoidOverlap = 0;
			int skippedLength = 0;
			int skippedSlope = 0;
			int skippedDiameter = 0;
			int skippedVertical = 0;
			int skippedGeometry = 0;
			int skippedCrowded = 0;
			int skippedError = 0;
			using Transaction transaction = new Transaction(base.Document, "Auto Tag Pipe");
			transaction.Start();
			foreach (ElementId pipeId in selectedPipeIds)
			{
				if (!(base.Document.GetElement(pipeId) is Pipe pipe))
				{
					skippedGeometry++;
				}
				else if (pipe.Location is LocationCurve { Curve: Line line })
				{
					if (IsVertical(line))
					{
						skippedVertical++;
						continue;
					}
					if (line.Length <= minLengthInternal)
					{
						skippedLength++;
						continue;
					}
					if (!TryGetPipeDiameter(pipe, out var diameter) || diameter > maxDnInternal)
					{
						skippedDiameter++;
						continue;
					}
					if (!TryGetPipeSlope(pipe, out var slopeRatio) || slopeRatio >= 0.45)
					{
						skippedSlope++;
						continue;
					}
					try
					{
						using SubTransaction subTransaction = new SubTransaction(base.Document);
						subTransaction.Start();
						try
						{
							XYZ tagPoint = line.Evaluate(0.5, normalized: true);
							Reference pipeReference = new Reference(pipe);
							IndependentTag tag = IndependentTag.Create(base.Document, activeView.Id, pipeReference, addLeader: false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, tagPoint);
							if (tag == null)
							{
								throw new System.InvalidOperationException("Revit không tạo được Pipe Tag.");
							}
							if (tag.GetTypeId() != window.SelectedTagTypeId)
							{
								tag.ChangeTypeId(window.SelectedTagTypeId);
							}
							if (TryGetPlanRotationAngle(line.Direction, activeView, out var angle) && Math.Abs(angle) > 1E-09)
							{
								Line axis = Line.CreateUnbound(tag.TagHeadPosition, activeView.ViewDirection);
								ElementTransformUtils.RotateElement(base.Document, tag.Id, axis, angle);
							}

							base.Document.Regenerate();
							if (!TryGetTagHeadOffsets(tag, activeView, out AutoTagPipeRectangle tagOffsets))
							{
								tagOffsets = CreateFallbackTagOffsets(activeView);
							}

							AutoTagPipePoint preferredHead = ToViewPoint(activeView, tag.TagHeadPosition);
							AutoTagPipePoint pipeDirection = ToViewDirection(activeView, line.Direction);
							int placementRings = Math.Min(
								MaximumPlacementRings,
								Math.Max(MinimumPlacementRings, occupiedTagBounds.Count * 2 + 16));
							AutoTagPipePlacement placement = AutoTagPipeCollisionLayout.FindNearestFreeHead(
								tagOffsets,
								preferredHead,
								pipeDirection,
								occupiedTagBounds,
								tagGapInternal,
								placementRings);
							if (!placement.Found)
							{
								subTransaction.RollBack();
								skippedCrowded++;
								continue;
							}

							if (placement.WasMoved)
							{
								tag.TagHeadPosition = MoveViewPoint(
									activeView,
									tag.TagHeadPosition,
									preferredHead,
									placement.Head);
								base.Document.Regenerate();
							}

							AutoTagPipeRectangle finalBounds;
							if (!TryGetElementBounds(tag, activeView, out finalBounds))
							{
								finalBounds = tagOffsets.Translate(placement.Head);
							}

							subTransaction.Commit();
							occupiedTagBounds.Add(finalBounds);
							taggedCount++;
							if (placement.WasMoved)
							{
								movedToAvoidOverlap++;
							}
						}
						catch
						{
							if (subTransaction.GetStatus() == TransactionStatus.Started)
							{
								subTransaction.RollBack();
							}
							throw;
						}
					}
					catch
					{
						skippedError++;
					}
				}
				else
				{
					skippedGeometry++;
				}
			}
			transaction.Commit();
			string report = $"Đã tag: {taggedCount}/{selectedPipeIds.Count}" + Environment.NewLine + $"Đã dịch để tránh chồng tag: {movedToAvoidOverlap}" + Environment.NewLine + $"Khoảng hở tag: {TagGapPaperMillimeters:0.###}mm trên bản in" + Environment.NewLine + $"Skip Length <= {window.SelectedLengthMinMillimeters:0.###}mm: {skippedLength}" + Environment.NewLine + $"Skip DN > {window.SelectedMaxDnMillimeters:0.###}mm hoặc không đọc được DN: {skippedDiameter}" + Environment.NewLine + $"Skip pipe thẳng đứng: {skippedVertical}" + Environment.NewLine + $"Skip Slope >= 45% hoặc không đọc được slope: {skippedSlope}" + Environment.NewLine + $"Skip hình học không hợp lệ: {skippedGeometry}" + Environment.NewLine + $"Skip vì không còn vùng trống an toàn: {skippedCrowded}" + Environment.NewLine + $"Skip lỗi tạo/rotate tag: {skippedError}";
			TaskDialog.Show("AutoTagPipe", report);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("AutoTagPipe", ex2.ToString());
		}
	}

	private static bool IsPipeTag(IndependentTag tag)
	{
		return tag.Category != null && tag.Category.Id.Value == -2008047;
	}

	private List<AutoTagPipeTagTypeItem> CollectPipeTagTypes()
	{
		return (from FamilySymbol symbol in new FilteredElementCollector(base.Document).OfClass(typeof(FamilySymbol))
			where symbol.Category != null && symbol.Category.Id.Value == -2008047
			orderby symbol.FamilyName, symbol.Name
			select new AutoTagPipeTagTypeItem(symbol.Id, symbol.FamilyName + " : " + symbol.Name)).ToList();
	}

	private static ElementId? ResolvePreferredTagTypeId(IndependentTag? sampleTag, IReadOnlyCollection<AutoTagPipeTagTypeItem> tagTypeItems)
	{
		if (sampleTag == null)
		{
			return null;
		}
		ElementId sampleTypeId = sampleTag.GetTypeId();
		return tagTypeItems.Any((AutoTagPipeTagTypeItem item) => item.Id == sampleTypeId) ? sampleTypeId : null;
	}

	private static bool TryGetPipeSlope(Pipe pipe, out double slopeRatio)
	{
		slopeRatio = 0.0;
		Parameter slopeParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
		if (slopeParameter == null)
		{
			return false;
		}
		try
		{
			slopeRatio = slopeParameter.AsDouble();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetPipeDiameter(Pipe pipe, out double diameter)
	{
		diameter = 0.0;
		Parameter diameterParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		if (diameterParameter == null)
		{
			return false;
		}
		try
		{
			diameter = diameterParameter.AsDouble();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsVertical(Line line)
	{
		XYZ direction = line.Direction.Normalize();
		double horizontal = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
		return horizontal <= 0.01;
	}

	private static bool TryGetPlanRotationAngle(XYZ direction, View view, out double angle)
	{
		angle = 0.0;
		XYZ viewDirection = view.ViewDirection.Normalize();
		XYZ projected = direction - viewDirection.Multiply(direction.DotProduct(viewDirection));
		if (projected.GetLength() < 1E-09)
		{
			return false;
		}
		XYZ normalized = projected.Normalize();
		XYZ right = view.RightDirection.Normalize();
		XYZ up = view.UpDirection.Normalize();
		double x = normalized.DotProduct(right);
		double y = normalized.DotProduct(up);
		angle = Math.Atan2(y, x);
		return true;
	}

	private static List<AutoTagPipeRectangle> CollectExistingTagBounds(
		Document document,
		View view)
	{
		List<AutoTagPipeRectangle> result = new List<AutoTagPipeRectangle>();
		foreach (IndependentTag tag in new FilteredElementCollector(document, view.Id)
			.OfClass(typeof(IndependentTag))
			.Cast<IndependentTag>())
		{
			if (TryGetElementBounds(tag, view, out AutoTagPipeRectangle bounds))
			{
				result.Add(bounds);
			}
		}
		return result;
	}

	private static bool TryGetTagHeadOffsets(
		IndependentTag tag,
		View view,
		out AutoTagPipeRectangle offsets)
	{
		offsets = default;
		if (!TryGetElementBounds(tag, view, out AutoTagPipeRectangle bounds))
		{
			return false;
		}
		AutoTagPipePoint head = ToViewPoint(view, tag.TagHeadPosition);
		offsets = new AutoTagPipeRectangle(
			bounds.MinX - head.X,
			bounds.MinY - head.Y,
			bounds.MaxX - head.X,
			bounds.MaxY - head.Y);
		return offsets.Width > Epsilon && offsets.Height > Epsilon;
	}

	private static AutoTagPipeRectangle CreateFallbackTagOffsets(View view)
	{
		double halfWidth = PaperMillimetersToInternal(18.0, view);
		double halfHeight = PaperMillimetersToInternal(4.0, view);
		return new AutoTagPipeRectangle(
			-halfWidth,
			-halfHeight,
			halfWidth,
			halfHeight);
	}

	private static bool TryGetElementBounds(
		Element element,
		View view,
		out AutoTagPipeRectangle rectangle)
	{
		rectangle = default;
		BoundingBoxXYZ box;
		try
		{
			box = element?.get_BoundingBox(view);
		}
		catch
		{
			return false;
		}
		if (box == null)
		{
			return false;
		}

		Transform transform = box.Transform ?? Transform.Identity;
		List<AutoTagPipePoint> points = new List<AutoTagPipePoint>();
		for (int x = 0; x <= 1; x++)
		{
			for (int y = 0; y <= 1; y++)
			{
				for (int z = 0; z <= 1; z++)
				{
					XYZ localPoint = new XYZ(
						x == 0 ? box.Min.X : box.Max.X,
						y == 0 ? box.Min.Y : box.Max.Y,
						z == 0 ? box.Min.Z : box.Max.Z);
					points.Add(ToViewPoint(view, transform.OfPoint(localPoint)));
				}
			}
		}

		rectangle = new AutoTagPipeRectangle(
			points.Min(point => point.X),
			points.Min(point => point.Y),
			points.Max(point => point.X),
			points.Max(point => point.Y));
		return rectangle.Width > Epsilon && rectangle.Height > Epsilon;
	}

	private static AutoTagPipePoint ToViewPoint(View view, XYZ point)
	{
		XYZ delta = point - view.Origin;
		return new AutoTagPipePoint(
			delta.DotProduct(view.RightDirection),
			delta.DotProduct(view.UpDirection));
	}

	private static AutoTagPipePoint ToViewDirection(View view, XYZ direction)
	{
		AutoTagPipePoint result = new AutoTagPipePoint(
			direction.DotProduct(view.RightDirection),
			direction.DotProduct(view.UpDirection));
		return result.Length > Epsilon
			? result
			: new AutoTagPipePoint(1.0, 0.0);
	}

	private static XYZ MoveViewPoint(
		View view,
		XYZ originalPoint,
		AutoTagPipePoint originalViewPoint,
		AutoTagPipePoint targetViewPoint)
	{
		return originalPoint +
			view.RightDirection.Multiply(targetViewPoint.X - originalViewPoint.X) +
			view.UpDirection.Multiply(targetViewPoint.Y - originalViewPoint.Y);
	}

	private static double PaperMillimetersToInternal(double millimeters, View view)
	{
		return UnitUtils.ConvertToInternalUnits(
			millimeters * Math.Max(1, view.Scale),
			UnitTypeId.Millimeters);
	}
}
