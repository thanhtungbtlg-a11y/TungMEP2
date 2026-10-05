using System.Collections.Generic;

namespace VP_RevitAPI.UI;

public sealed class ParameterSelectionGroup
{
	public string Name { get; }

	public List<ParameterSelectionItem> Items { get; } = new List<ParameterSelectionItem>();

	public ParameterSelectionGroup(string name)
	{
		Name = name;
	}
}
