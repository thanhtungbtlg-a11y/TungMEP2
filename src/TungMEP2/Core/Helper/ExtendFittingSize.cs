using System;

namespace VP_RevitAPI.Helper;

public readonly struct ExtendFittingSize
{
	public double? DiameterInternal { get; }

	public double? WidthInternal { get; }

	public double? HeightInternal { get; }

	public double ScaleSizeInternal
	{
		get
		{
			if (DiameterInternal.HasValue && DiameterInternal.Value > 0.0)
			{
				return DiameterInternal.Value;
			}
			double width = WidthInternal.GetValueOrDefault();
			double height = HeightInternal.GetValueOrDefault();
			return Math.Max(width, height);
		}
	}

	public ExtendFittingSize(double? diameterInternal, double? widthInternal, double? heightInternal)
	{
		DiameterInternal = diameterInternal;
		WidthInternal = widthInternal;
		HeightInternal = heightInternal;
	}
}
