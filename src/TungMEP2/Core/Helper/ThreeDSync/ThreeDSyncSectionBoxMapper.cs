using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.ThreeDSync;

public static class ThreeDSyncSectionBoxMapper
{
	public static BoundingBoxXYZ UpdateXY(BoundingBoxXYZ currentSectionBox, XYZ corner1, XYZ corner2)
	{
		double worldMinX = ((corner1.X < corner2.X) ? corner1.X : corner2.X);
		double worldMinY = ((corner1.Y < corner2.Y) ? corner1.Y : corner2.Y);
		double worldMaxX = ((corner1.X > corner2.X) ? corner1.X : corner2.X);
		double worldMaxY = ((corner1.Y > corner2.Y) ? corner1.Y : corner2.Y);
		double worldZ = currentSectionBox.Transform.Origin.Z;
		Transform inverse = currentSectionBox.Transform.Inverse;
		XYZ[] localCorners = new XYZ[4]
		{
			inverse.OfPoint(new XYZ(worldMinX, worldMinY, worldZ)),
			inverse.OfPoint(new XYZ(worldMinX, worldMaxY, worldZ)),
			inverse.OfPoint(new XYZ(worldMaxX, worldMinY, worldZ)),
			inverse.OfPoint(new XYZ(worldMaxX, worldMaxY, worldZ))
		};
		double localMinX = localCorners.Min((XYZ point) => point.X);
		double localMinY = localCorners.Min((XYZ point) => point.Y);
		double localMaxX = localCorners.Max((XYZ point) => point.X);
		double localMaxY = localCorners.Max((XYZ point) => point.Y);
		return new BoundingBoxXYZ
		{
			Min = new XYZ(localMinX, localMinY, currentSectionBox.Min.Z),
			Max = new XYZ(localMaxX, localMaxY, currentSectionBox.Max.Z),
			Transform = CloneTransform(currentSectionBox.Transform)
		};
	}

	private static Transform CloneTransform(Transform source)
	{
		Transform transform = Transform.Identity;
		transform.Origin = source.Origin;
		transform.BasisX = source.BasisX;
		transform.BasisY = source.BasisY;
		transform.BasisZ = source.BasisZ;
		return transform;
	}
}
