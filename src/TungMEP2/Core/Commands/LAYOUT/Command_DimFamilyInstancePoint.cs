using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_DimFamilyInstancePoint : SecuredExternalCommand
{
	private const string DialogTitle = "DimFamilyInstancePoint";

	private const double DirectionTolerance = 0.99;

	private const double MinimumHalfLength = 20.0;

	private const double MinimumPickDistance = 1E-06;

	protected override void ExecuteCore()
	{
		try
		{
			List<Element> selectedElements = (from element in base.UiDocument.Selection.GetElementIds().Select(base.Document.GetElement)
				where element != null
				select element).Cast<Element>().ToList();
			List<FamilyInstance> familyInstances = selectedElements.OfType<FamilyInstance>().ToList();
			if (familyInstances.Count < 2)
			{
				TaskDialog.Show("DimFamilyInstancePoint", "Vui lòng chọn ít nhất 2 FamilyInstance để tạo DIM.");
			}
			else
			{
				if (!TryResolveDimensionLine(familyInstances, out Line dimensionLine))
				{
					return;
				}
				Options options = new Options
				{
					ComputeReferences = true,
					IncludeNonVisibleObjects = true,
					DetailLevel = ViewDetailLevel.Fine
				};
				ReferenceArray referenceArray = CollectReferences(familyInstances, options, dimensionLine.Direction);
				if (referenceArray.Size >= 2)
				{
					using (Transaction transaction = new Transaction(base.Document, "AutoDim FamilyInstance Point"))
					{
						transaction.Start();
						base.Document.Create.NewDimension(base.Document.ActiveView, dimensionLine, referenceArray);
						transaction.Commit();
						return;
					}
				}
				TaskDialog.Show("DimFamilyInstancePoint", "Không đủ reference hợp lệ để tạo DIM.");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("DimFamilyInstancePoint", ex2.ToString());
		}
	}

	private bool TryResolveDimensionLine(IReadOnlyList<FamilyInstance> familyInstances, out Line? dimensionLine)
	{
		dimensionLine = null;
		XYZ startPoint = base.UiDocument.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm đầu để đặt trục DIM.");
		XYZ endPoint = base.UiDocument.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm thứ hai để xác định hướng DIM.");
		if (startPoint.DistanceTo(endPoint) < 1E-06)
		{
			TaskDialog.Show("DimFamilyInstancePoint", "Hai điểm quá gần nhau. Vui lòng chọn lại.");
			return false;
		}
		if (!TryGetClosestAxisDirection(startPoint, endPoint, out XYZ axisDirection))
		{
			TaskDialog.Show("DimFamilyInstancePoint", "Không xác định được hướng DIM theo view hiện tại.");
			return false;
		}
		dimensionLine = CreateLineThroughPoint(startPoint, axisDirection, familyInstances);
		return true;
	}

	private bool TryGetClosestAxisDirection(XYZ startPoint, XYZ endPoint, out XYZ direction)
	{
		direction = XYZ.Zero;
		XYZ candidateDirection = endPoint - startPoint;
		if (!TryNormalize(candidateDirection, out XYZ normalizedCandidate))
		{
			return false;
		}
		if (!TryNormalize(base.Document.ActiveView.RightDirection, out XYZ viewXDirection))
		{
			return false;
		}
		if (!TryNormalize(base.Document.ActiveView.UpDirection, out XYZ viewYDirection))
		{
			return false;
		}
		double xScore = Math.Abs(normalizedCandidate.DotProduct(viewXDirection));
		double yScore = Math.Abs(normalizedCandidate.DotProduct(viewYDirection));
		direction = ((yScore >= xScore) ? viewYDirection : viewXDirection);
		return true;
	}

	private static Line CreateLineThroughPoint(XYZ point, XYZ direction, IReadOnlyList<FamilyInstance> familyInstances)
	{
		double halfLength = GetHalfLengthFromInstances(familyInstances);
		XYZ start = point - direction.Multiply(halfLength);
		XYZ end = point + direction.Multiply(halfLength);
		return Line.CreateBound(start, end);
	}

	private static double GetHalfLengthFromInstances(IReadOnlyList<FamilyInstance> familyInstances)
	{
		XYZ min = null;
		XYZ max = null;
		foreach (FamilyInstance instance in familyInstances)
		{
			BoundingBoxXYZ boundingBox = ((Element)instance).get_BoundingBox((View)null);
			if (boundingBox != null)
			{
				min = ((min == null) ? boundingBox.Min : new XYZ(Math.Min(min.X, boundingBox.Min.X), Math.Min(min.Y, boundingBox.Min.Y), Math.Min(min.Z, boundingBox.Min.Z)));
				max = ((max == null) ? boundingBox.Max : new XYZ(Math.Max(max.X, boundingBox.Max.X), Math.Max(max.Y, boundingBox.Max.Y), Math.Max(max.Z, boundingBox.Max.Z)));
			}
		}
		if (min == null || max == null)
		{
			return 20.0;
		}
		double diagonal = min.DistanceTo(max);
		return Math.Max(20.0, diagonal);
	}

	private static ReferenceArray CollectReferences(IReadOnlyList<FamilyInstance> familyInstances, Options options, XYZ dimensionDirection)
	{
		ReferenceArray referenceArray = new ReferenceArray();
		if (!TryNormalize(dimensionDirection, out XYZ normalizedDirection))
		{
			return referenceArray;
		}
		foreach (FamilyInstance familyInstance in familyInstances)
		{
			Reference reference = TryGetPointReference(familyInstance, options) ?? TryGetCenterReferenceByDirection(familyInstance, normalizedDirection);
			if (reference != null)
			{
				referenceArray.Append(reference);
			}
		}
		return referenceArray;
	}

	private static Reference? TryGetPointReference(FamilyInstance familyInstance, Options options)
	{
		GeometryElement geometryElement = ((Element)familyInstance).get_Geometry(options);
		if (geometryElement == null)
		{
			return null;
		}
		return TryFindPointReference(geometryElement);
	}

	private static Reference? TryFindPointReference(IEnumerable<GeometryObject> geometryObjects)
	{
		foreach (GeometryObject geometryObject in geometryObjects)
		{
			if (geometryObject is Point { Reference: not null, Reference: var reference })
			{
				return reference;
			}
			if (geometryObject is GeometryInstance geometryInstance)
			{
				Reference nestedReference = TryFindPointReference(geometryInstance.GetInstanceGeometry());
				if (nestedReference != null)
				{
					return nestedReference;
				}
			}
		}
		return null;
	}

	private static Reference? TryGetCenterReferenceByDirection(FamilyInstance instance, XYZ dimensionDirection)
	{
		Reference centerFrontBack = instance.GetReferences(FamilyInstanceReferenceType.CenterFrontBack).FirstOrDefault();
		Reference centerLeftRight = instance.GetReferences(FamilyInstanceReferenceType.CenterLeftRight).FirstOrDefault();
		if (centerFrontBack == null && centerLeftRight == null)
		{
			return null;
		}
		double frontBackScore = GetDirectionScore(instance.FacingOrientation, dimensionDirection, centerFrontBack != null);
		double leftRightScore = GetDirectionScore(instance.HandOrientation, dimensionDirection, centerLeftRight != null);
		if (frontBackScore >= leftRightScore)
		{
			return (frontBackScore >= 0.99) ? centerFrontBack : null;
		}
		return (leftRightScore >= 0.99) ? centerLeftRight : null;
	}

	private static double GetDirectionScore(XYZ orientation, XYZ direction, bool hasReference)
	{
		if (!hasReference)
		{
			return -1.0;
		}
		if (!TryNormalize(orientation, out XYZ normalizedOrientation))
		{
			return -1.0;
		}
		return Math.Abs(normalizedOrientation.DotProduct(direction));
	}

	private static bool TryNormalize(XYZ vector, out XYZ normalized)
	{
		normalized = XYZ.Zero;
		if (vector == null || vector.IsZeroLength())
		{
			return false;
		}
		normalized = vector.Normalize();
		return !normalized.IsZeroLength();
	}
}
