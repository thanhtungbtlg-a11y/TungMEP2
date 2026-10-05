using Autodesk.Revit.DB;

namespace VP_RevitAPI.State;

public sealed class SyncViewState
{
	public string DocumentKey { get; init; } = string.Empty;

	public SyncViewKind Kind { get; init; }

	public XYZ Corner1 { get; init; } = XYZ.Zero;

	public XYZ Corner2 { get; init; } = XYZ.Zero;

	public ViewOrientation3D? Orientation3D { get; init; }

	public bool IsPerspective { get; init; }

	public bool IsSectionBoxActive { get; init; }

	public BoundingBoxXYZ? SectionBox { get; init; }
}
