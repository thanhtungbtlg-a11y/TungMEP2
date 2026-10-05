using System;
using System.Collections.Generic;

namespace VP_RevitAPI.State;

public sealed class ThreeDSyncViewSelectionConfig
{
	public string DocumentKey { get; init; } = string.Empty;

	public string PlanViewUniqueId { get; init; } = string.Empty;

	public string PlanViewName { get; init; } = string.Empty;

	public IReadOnlyList<ThreeDSyncViewReference> ThreeDViews { get; init; } = Array.Empty<ThreeDSyncViewReference>();
}
