using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Electrical;

internal sealed class CircuitWireRebuildResult
{
	internal int DeletedWireCount { get; set; }

	internal int CreatedWireCount { get; set; }

	internal List<ElementId> FailedElementIds { get; } = new List<ElementId>();
}
