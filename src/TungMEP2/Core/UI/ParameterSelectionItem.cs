namespace VP_RevitAPI.UI;

public sealed class ParameterSelectionItem
{
	public string Name { get; }

	public bool IsChecked { get; set; }

	public ParameterSelectionItem(string name, bool isChecked)
	{
		Name = name;
		IsChecked = isChecked;
	}
}
