using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets;

public static class CropRegionFromSheetBoundary
{
	private const double CurveMatchTolerance = 1.0 / 192.0;

	public static void Apply(Document document, Viewport viewport, IList<CurveElement> boundaryCurves)
	{
		View view = (document.GetElement(viewport.ViewId) as View) ?? throw new InvalidOperationException("Không thể lấy view từ viewport được chọn.");
		if (!view.CropBoxActive)
		{
			using Transaction txActivate = new Transaction(document, "Activate Crop Box");
			txActivate.Start();
			view.CropBoxActive = true;
			txActivate.Commit();
		}
		ViewCropRegionShapeManager cropShapeManager = view.GetCropRegionShapeManager();
		CurveLoop existingCrop = cropShapeManager.GetCropShape().FirstOrDefault();
		if (existingCrop == null)
		{
			throw new InvalidOperationException("View không hỗ trợ Crop Region shape.");
		}
		List<Line> rawLines = (from boundary in boundaryCurves
			select boundary.GeometryCurve into curve
			select Line.CreateBound(curve.GetEndPoint(0), curve.GetEndPoint(1))).ToList();
		List<Line> sortedLines = SortCurvesContiguous(rawLines);
		if (sortedLines == null)
		{
			throw new InvalidOperationException("Các đường biên phải tạo thành một vòng kín liên tục.");
		}
		CurveLoop sourceBoundary = new CurveLoop();
		foreach (Line line in sortedLines)
		{
			sourceBoundary.Append(line);
		}
		Transform transform = GetTransformation(viewport, view, existingCrop);
		CurveLoop transformedBoundary = CurveLoop.CreateViaTransform(sourceBoundary, transform);
		using Transaction txSet = new Transaction(document, "Set Crop Region");
		txSet.Start();
		cropShapeManager.SetCropShape(transformedBoundary);
		txSet.Commit();
	}

	private static List<Line>? SortCurvesContiguous(List<Line> curves)
	{
		int n = curves.Count;
		for (int i = 0; i < n; i++)
		{
			Line current = curves[i];
			XYZ endpoint = current.GetEndPoint(1);
			bool found = i + 1 >= n;
			for (int j = i + 1; j < n; j++)
			{
				XYZ nextStart = curves[j].GetEndPoint(0);
				if (nextStart.DistanceTo(endpoint) < 1.0 / 192.0)
				{
					if (i + 1 != j)
					{
						int index = i + 1;
						int index2 = j;
						Line value = curves[j];
						Line value2 = curves[i + 1];
						curves[index] = value;
						curves[index2] = value2;
					}
					found = true;
					break;
				}
				XYZ nextEnd = curves[j].GetEndPoint(1);
				if (nextEnd.DistanceTo(endpoint) < 1.0 / 192.0)
				{
					Line reversed = CreateReversed(curves[j]);
					if (i + 1 == j)
					{
						curves[i + 1] = reversed;
					}
					else
					{
						Line tmp = curves[i + 1];
						curves[i + 1] = reversed;
						curves[j] = tmp;
					}
					found = true;
					break;
				}
			}
			if (!found)
			{
				return null;
			}
		}
		XYZ firstStart = curves[0].GetEndPoint(0);
		XYZ lastEnd = curves[n - 1].GetEndPoint(1);
		return (firstStart.DistanceTo(lastEnd) < 1.0 / 192.0) ? curves : null;
	}

	private static Line CreateReversed(Line line)
	{
		return Line.CreateBound(line.GetEndPoint(1), line.GetEndPoint(0));
	}

	private static Transform GetTransformation(Viewport viewport, View view, CurveLoop viewCropShape)
	{
		(XYZ max, XYZ min) minMax = GetMinMax(viewCropShape);
		XYZ cropMax = minMax.max;
		XYZ cropMin = minMax.min;
		Outline outline = viewport.GetBoxOutline();
		XYZ sheetMax = outline.MaximumPoint;
		XYZ sheetMin = outline.MinimumPoint;
		XYZ cropCenter = (cropMax + cropMin) * 0.5;
		XYZ sheetCenter = (sheetMax + sheetMin) * 0.5;
		Transform sheetTransform = Transform.CreateTranslation(-sheetCenter);
		Transform basisTransform = Transform.CreateTranslation(cropCenter);
		basisTransform.BasisX = view.RightDirection * view.Scale;
		basisTransform.BasisY = view.UpDirection * view.Scale;
		basisTransform.BasisZ = view.ViewDirection * view.Scale;
		return basisTransform.Multiply(sheetTransform);
	}

	private static (XYZ max, XYZ min) GetMinMax(CurveLoop shape)
	{
		XYZ max = new XYZ(double.MinValue, double.MinValue, double.MinValue);
		XYZ min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue);
		foreach (Curve curve in shape)
		{
			XYZ p0 = curve.GetEndPoint(0);
			XYZ p1 = curve.GetEndPoint(1);
			max = new XYZ(Math.Max(max.X, Math.Max(p0.X, p1.X)), Math.Max(max.Y, Math.Max(p0.Y, p1.Y)), Math.Max(max.Z, Math.Max(p0.Z, p1.Z)));
			min = new XYZ(Math.Min(min.X, Math.Min(p0.X, p1.X)), Math.Min(min.Y, Math.Min(p0.Y, p1.Y)), Math.Min(min.Z, Math.Min(p0.Z, p1.Z)));
		}
		return (max: max, min: min);
	}
}
