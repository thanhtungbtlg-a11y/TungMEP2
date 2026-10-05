namespace VP_RevitAPI.UI;

public sealed class UI_Request
{
	public UI_RequestKind Kind { get; init; }

	public string? Message { get; init; }

	public SectionBoxFace? SectionBoxFace { get; init; }
}
