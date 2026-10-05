using Autodesk.Revit.DB;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Helper.SectionBox;

public static class SectionBoxSlideMath
{
	public static bool TrySlideFace(SectionBoxFace face, BoundingBoxXYZ sectionBox, XYZ pickedPointWorld, out BoundingBoxXYZ updatedSectionBox, out string? error)
	{
		updatedSectionBox = CloneSectionBox(sectionBox);
		error = null;
		if (!TryResolveFaceMapping(face, out var axisIndex, out var moveMax))
		{
			error = "Không xác định được mặt cần trượt.";
			return false;
		}
		Transform inverse = sectionBox.Transform.Inverse;
		XYZ pickedPointLocal = inverse.OfPoint(pickedPointWorld);
		XYZ newMin = updatedSectionBox.Min;
		XYZ newMax = updatedSectionBox.Max;
		double target = GetAxisValue(pickedPointLocal, axisIndex);
		if (moveMax)
		{
			newMax = SetAxisValue(newMax, axisIndex, target);
		}
		else
		{
			newMin = SetAxisValue(newMin, axisIndex, target);
		}
		double minValue = GetAxisValue(newMin, axisIndex);
		double maxValue = GetAxisValue(newMax, axisIndex);
		if (minValue > maxValue)
		{
			error = "Điểm vượt qua mặt đối diện, không thể trượt mặt đã chọn.";
			return false;
		}
		updatedSectionBox.Min = newMin;
		updatedSectionBox.Max = newMax;
		return true;
	}

	private static bool TryResolveFaceMapping(SectionBoxFace face, out int axisIndex, out bool moveMax)
	{
		switch (face)
		{
		case SectionBoxFace.Left:
			axisIndex = 0;
			moveMax = false;
			return true;
		case SectionBoxFace.Right:
			axisIndex = 0;
			moveMax = true;
			return true;
		case SectionBoxFace.Front:
			axisIndex = 1;
			moveMax = false;
			return true;
		case SectionBoxFace.Back:
			axisIndex = 1;
			moveMax = true;
			return true;
		case SectionBoxFace.Top:
			axisIndex = 2;
			moveMax = true;
			return true;
		case SectionBoxFace.Bottom:
			axisIndex = 2;
			moveMax = false;
			return true;
		default:
			axisIndex = 0;
			moveMax = false;
			return false;
		}
	}

	private static double GetAxisValue(XYZ point, int axisIndex)
	{
		if (1 == 0)
		{
		}
		double result = axisIndex switch
		{
			0 => point.X, 
			1 => point.Y, 
			_ => point.Z, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static XYZ SetAxisValue(XYZ point, int axisIndex, double value)
	{
		if (1 == 0)
		{
		}
		XYZ result = axisIndex switch
		{
			0 => new XYZ(value, point.Y, point.Z), 
			1 => new XYZ(point.X, value, point.Z), 
			_ => new XYZ(point.X, point.Y, value), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static BoundingBoxXYZ CloneSectionBox(BoundingBoxXYZ source)
	{
		return new BoundingBoxXYZ
		{
			Min = source.Min,
			Max = source.Max,
			Transform = source.Transform
		};
	}
}
