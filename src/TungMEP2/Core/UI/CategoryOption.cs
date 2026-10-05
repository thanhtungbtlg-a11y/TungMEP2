namespace VP_RevitAPI.UI;

public sealed class CategoryOption
{
	public string Name { get; }

	public int Count { get; }

	public bool IsChecked { get; set; }

	public string DisplayName => $"{Name} ({Count})";

	public CategoryOption(string name, int count)
	{
		Name = (string.IsNullOrWhiteSpace(name) ? "(No Category)" : name.Trim());
		Count = count;
	}
}
