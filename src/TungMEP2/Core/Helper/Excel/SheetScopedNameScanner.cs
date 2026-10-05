using System;
using System.Collections.Generic;
using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel;

internal static class SheetScopedNameScanner
{
	public static void ForEachDefinedName(Workbook workbook, Action<Worksheet?, Name, bool> callback, ICollection<string>? cleanupWarnings = null)
	{
		Names names = null;
		try
		{
			names = workbook.Names;
			foreach (object nameObject in names)
			{
				if (!(nameObject is Name name))
				{
					continue;
				}
				object parent = null;
				Worksheet worksheet = null;
				try
				{
					parent = name.Parent;
					bool isWorkbookScope = parent is Workbook;
					worksheet = parent as Worksheet;
					callback(worksheet, name, isWorkbookScope);
				}
				finally
				{
					if (!(parent is Workbook))
					{
						ExcelInteropSession.TryRelease(parent);
					}
					ExcelInteropSession.TryRelease(name, cleanupWarnings ?? new List<string>(), "NameRelease");
				}
			}
		}
		finally
		{
			ExcelInteropSession.TryRelease(names);
		}
	}
}
