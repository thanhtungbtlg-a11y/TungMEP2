using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelLegendCheckerScanSummary(int Total, int Fresh, int Stale, int FileError, IReadOnlyList<ExcelLegendCheckerRow> Rows);
