using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

internal sealed class AutoDimDatabaseSnapshot
{
	public RevitLinkInstance ArcLink { get; }

	public RevitLinkInstance StrLink { get; }

	public Transform ArcTransform { get; }

	public Transform StrTransform { get; }

	public IReadOnlyList<Wall> Walls { get; }

	public IReadOnlyList<FamilyInstance> Columns { get; }

	public IReadOnlyList<FamilyInstance> Framings { get; }

	public double DistanceMillimeters { get; }

	public AutoDimDatabaseSnapshot(RevitLinkInstance arcLink, RevitLinkInstance strLink, Transform arcTransform, Transform strTransform, IReadOnlyList<Wall> walls, IReadOnlyList<FamilyInstance> columns, IReadOnlyList<FamilyInstance> framings, double distanceMillimeters)
	{
		ArcLink = arcLink;
		StrLink = strLink;
		ArcTransform = arcTransform;
		StrTransform = strTransform;
		Walls = walls;
		Columns = columns;
		Framings = framings;
		DistanceMillimeters = distanceMillimeters;
	}
}
