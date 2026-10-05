using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class FitRotateRotationGeometry
{
	internal static bool CanRotateConnectedBranch(
		XYZ axisStart,
		XYZ axisEnd,
		XYZ pivot,
		XYZ movingDirection,
		double tolerance)
	{
		if (axisStart == null ||
			axisEnd == null ||
			pivot == null ||
			movingDirection == null ||
			tolerance <= 0.0)
		{
			return false;
		}

		XYZ axisVector = axisEnd - axisStart;
		double axisLength = axisVector.GetLength();
		if (axisLength <= tolerance || movingDirection.GetLength() <= tolerance)
		{
			return false;
		}

		XYZ axisDirection = axisVector / axisLength;
		double distanceToAxis = (pivot - axisStart)
			.CrossProduct(axisDirection)
			.GetLength();

		// The downstream branch may form any angle with the rotation axis. The
		// connection pivot merely has to lie on the actual 3D axis.
		return distanceToAxis <= tolerance;
	}
}
