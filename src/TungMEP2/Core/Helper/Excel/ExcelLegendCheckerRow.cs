namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelLegendCheckerRow(long LegendId, string LegendName, string CurrentPath, string CurrentScopedDefinedName, ExcelLegendCheckerStatus Status, string Message);
