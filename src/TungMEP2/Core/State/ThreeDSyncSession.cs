using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.State;

public sealed class ThreeDSyncSession
{
	public string DocumentKey { get; init; } = string.Empty;

	public ElementId PlanViewId { get; init; } = ElementId.InvalidElementId;

	public IReadOnlyList<ElementId> View3DIds { get; init; } = Array.Empty<ElementId>();

	public XYZ Corner1 { get; set; } = XYZ.Zero;

	public XYZ Corner2 { get; set; } = XYZ.Zero;
}
