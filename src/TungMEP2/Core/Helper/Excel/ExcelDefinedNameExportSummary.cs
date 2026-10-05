using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelDefinedNameExportSummary(string WorkbookPath, string Quality, DateTime StartedAtUtc, DateTime EndedAtUtc, int Total, int Exported, int Skipped, int Failed, IReadOnlyList<ExcelDefinedNameExportItemResult> Items);
