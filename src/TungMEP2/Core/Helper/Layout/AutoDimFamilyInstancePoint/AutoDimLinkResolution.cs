using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

internal sealed class AutoDimLinkResolution
{
	public RevitLinkInstance? SuggestedArc { get; }

	public RevitLinkInstance? SuggestedStr { get; }

	public string Message { get; }

	public AutoDimLinkResolution(RevitLinkInstance? suggestedArc, RevitLinkInstance? suggestedStr, string message)
	{
		SuggestedArc = suggestedArc;
		SuggestedStr = suggestedStr;
		Message = message;
	}
}
