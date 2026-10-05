using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class LeaderElbowCalculator
{
	public static XYZ Calculate(XYZ right, XYZ up, XYZ viewDir, XYZ head, XYZ end, LeaderElbowMode mode)
	{
		XYZ normalizedRight = right.Normalize();
		XYZ normalizedUp = up.Normalize();
		XYZ normalizedViewDir = viewDir.Normalize();
		double uHead = head.DotProduct(normalizedRight);
		double vHead = head.DotProduct(normalizedUp);
		double wHead = head.DotProduct(normalizedViewDir);
		double uEnd = end.DotProduct(normalizedRight);
		double vEnd = end.DotProduct(normalizedUp);
		return (mode == LeaderElbowMode.HorizontalFirst) ? (normalizedRight * uEnd + normalizedUp * vHead + normalizedViewDir * wHead) : (normalizedRight * uHead + normalizedUp * vEnd + normalizedViewDir * wHead);
	}
}
