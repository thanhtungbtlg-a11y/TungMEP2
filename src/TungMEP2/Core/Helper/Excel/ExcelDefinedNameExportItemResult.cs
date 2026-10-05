namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelDefinedNameExportItemResult(string SheetName, string DefinedName, string? OutputPath, bool IsExported, ExcelDefinedNameExportReasonCode? ReasonCode, string Message, bool IsInformational = false);
