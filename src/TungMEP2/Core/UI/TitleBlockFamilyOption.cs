using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.UI;

public sealed class TitleBlockFamilyOption
{
	public string Name { get; }

	public IReadOnlyList<TitleBlockTypeOption> Types { get; }

	public TitleBlockFamilyOption(string name, IList<TitleBlockTypeOption> types)
	{
		Name = (string.IsNullOrWhiteSpace(name) ? "(Không rõ Family)" : name.Trim());
		Types = (types ?? Array.Empty<TitleBlockTypeOption>()).ToList();
	}
}
