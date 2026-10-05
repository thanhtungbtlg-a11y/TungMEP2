namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelLegendLinkItemResult(long? LegendId, string LegendName, string ExcelPath, string DefinedName, ExcelLegendLinkAction Action, string Message, bool IsWarning = false);
