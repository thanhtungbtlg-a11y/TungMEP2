namespace VP_RevitAPI.Helper.Sheets.AlignViewports;

public sealed class AlignViewportsOptions
{
	public bool ApplySameCropScopeBox { get; set; } = true;

	public bool OverlapMultipleViewPlans { get; set; } = true;

	public bool AlignLegendIfSame { get; set; } = true;

	public bool AlignViewportTitlesXY { get; set; } = true;

	public bool ApplySameTitleBlockType { get; set; }
}
