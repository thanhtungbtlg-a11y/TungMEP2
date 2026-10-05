using System;

namespace VP_RevitAPI.Helper;

public static class ExtendFittingConnectorSizing
{
	public static ExtendFittingSize FromRound(double radiusInternal)
	{
		double diameterInternal = ((radiusInternal > 0.0) ? (radiusInternal * 2.0) : 0.0);
		return (diameterInternal > 0.0) ? new ExtendFittingSize(diameterInternal, null, null) : new ExtendFittingSize(null, null, null);
	}

	public static ExtendFittingSize FromRect(double widthInternal, double heightInternal)
	{
		double safeWidth = ((widthInternal > 0.0) ? widthInternal : 0.0);
		double safeHeight = ((heightInternal > 0.0) ? heightInternal : 0.0);
		return (safeWidth > 0.0 || safeHeight > 0.0) ? new ExtendFittingSize(null, (safeWidth > 0.0) ? new double?(safeWidth) : ((double?)null), (safeHeight > 0.0) ? new double?(safeHeight) : ((double?)null)) : new ExtendFittingSize(null, null, null);
	}

	public static double ComputeStubLengthInternal(double baseLenInternal, double scaleSizeInternal)
	{
		return Math.Max(baseLenInternal, 3.0 * scaleSizeInternal);
	}
}
