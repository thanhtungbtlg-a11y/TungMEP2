using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.UI;

public sealed class TransTempLinkItem
{
	public string DisplayName { get; }

	public Document Document { get; }

	public IReadOnlyList<TransTempTemplateItem> Templates { get; }

	public TransTempLinkItem(string displayName, Document document, IList<TransTempTemplateItem> templates)
	{
		DisplayName = displayName ?? string.Empty;
		Document = document;
		Templates = (templates ?? Array.Empty<TransTempTemplateItem>()).ToList();
	}
}
