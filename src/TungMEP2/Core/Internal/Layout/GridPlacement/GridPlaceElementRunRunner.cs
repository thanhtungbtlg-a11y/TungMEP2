using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace VP_RevitAPI.Internal.Layout.GridPlacement;

internal static class GridPlaceElementRunRunner
{
	private const string DialogTitle = "GridPlace";

	private const int MaxPlacementCount = 5000;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		if (!TryResolveSettings(document, out FamilySymbol templateSymbol, out double templateZ, out ElementId templateLevelId, out double spacingMillimeters, out string error))
		{
			TaskDialog.Show("GridPlace", error);
			return;
		}
		XYZ point1 = uiDocument.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm gốc (Point 1).");
		XYZ point2 = uiDocument.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm định hướng (Point 2).");
		double spacingInternal = UnitUtils.ConvertToInternalUnits(spacingMillimeters, UnitTypeId.Millimeters);
		if (spacingInternal <= 0.0)
		{
			TaskDialog.Show("GridPlace", "Khoảng cách không hợp lệ. Vui lòng chạy Settings và nhập giá trị > 0.");
			return;
		}
		double lengthX = point2.X - point1.X;
		double lengthY = point2.Y - point1.Y;
		double signX = GridPlacementMath.GetDirectionSign(lengthX);
		double signY = GridPlacementMath.GetDirectionSign(lengthY);
		double halfSpacing = spacingInternal / 2.0;
		int countX = GridPlacementMath.CalculateCount(lengthX, spacingInternal);
		int countY = GridPlacementMath.CalculateCount(lengthY, spacingInternal);
		int totalCells = countX * countY;
		if (totalCells > 5000)
		{
			TaskDialog.Show("GridPlace", $"Số lượng phần tử dự kiến ({totalCells}) vượt giới hạn {5000}. Giảm vùng rải hoặc tăng spacing.");
			return;
		}
		double startX = point1.X + halfSpacing * signX;
		double startY = point1.Y + halfSpacing * signY;
		List<ElementId> createdElementIds = new List<ElementId>(totalCells);
		using Transaction transaction = new Transaction(document, "GridPlace Element");
		transaction.Start();
		if (!templateSymbol.IsActive)
		{
			templateSymbol.Activate();
			document.Regenerate();
		}
		for (int ix = 0; ix < countX; ix++)
		{
			for (int iy = 0; iy < countY; iy++)
			{
				double targetX = startX + (double)ix * spacingInternal * signX;
				double targetY = startY + (double)iy * spacingInternal * signY;
				XYZ targetPoint = new XYZ(targetX, targetY, templateZ);
				Element createdElement = PlaceInstance(document, templateSymbol, templateLevelId, targetPoint);
				if (createdElement != null)
				{
					createdElementIds.Add(createdElement.Id);
				}
			}
		}
		transaction.Commit();
		if (createdElementIds.Count > 0)
		{
			uiDocument.Selection.SetElementIds(createdElementIds);
		}
	}

	private static bool TryResolveSettings(Document document, out FamilySymbol? templateSymbol, out double templateZ, out ElementId templateLevelId, out double spacingMillimeters, out string error)
	{
		templateSymbol = null;
		templateZ = 0.0;
		templateLevelId = ElementId.InvalidElementId;
		spacingMillimeters = 0.0;
		error = string.Empty;
		if (!GridPlaceElementState.TryGetTemplateTypeId(out ElementId templateTypeId))
		{
			error = "Chưa có Family Type mẫu. Vui lòng chạy GridPlace Settings trước.";
			return false;
		}
		if (!GridPlaceElementState.TryGetSpacingMillimeters(out spacingMillimeters) || spacingMillimeters <= 0.0)
		{
			error = "Chưa có spacing hợp lệ. Vui lòng chạy GridPlace Settings trước.";
			return false;
		}
		if (!GridPlaceElementState.TryGetTemplateZ(out templateZ))
		{
			error = "Chưa có thông tin cao độ Z mẫu. Vui lòng chạy GridPlace Settings trước.";
			return false;
		}
		GridPlaceElementState.TryGetTemplateLevelId(out templateLevelId);
		templateSymbol = document.GetElement(templateTypeId) as FamilySymbol;
		if (templateSymbol == null)
		{
			error = "Không tìm thấy Family Type mẫu trong document hiện tại. Vui lòng chạy lại GridPlace Settings.";
			return false;
		}
		return true;
	}

	private static Element? PlaceInstance(Document document, FamilySymbol symbol, ElementId levelId, XYZ point)
	{
		Level level = ((levelId != ElementId.InvalidElementId) ? (document.GetElement(levelId) as Level) : null);
		if (level != null)
		{
			return document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
		}
		return document.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural);
	}
}
