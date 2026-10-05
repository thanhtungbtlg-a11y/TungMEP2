using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class AutoDimCacheSettings
{
	public RevitLinkInstance ArcLink { get; }

	public RevitLinkInstance StrLink { get; }

	public double DistanceMillimeters { get; }

	public AutoDimCacheSettings(RevitLinkInstance arcLink, RevitLinkInstance strLink, double distanceMillimeters)
	{
		ArcLink = arcLink;
		StrLink = strLink;
		DistanceMillimeters = distanceMillimeters;
	}
}
