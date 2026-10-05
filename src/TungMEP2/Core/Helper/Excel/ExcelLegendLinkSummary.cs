using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelLegendLinkSummary(int Scanned, int Created, int Updated, int Unchanged, int StaleFound, int Orphaned, int ParseError, int Failed, IReadOnlyList<ExcelLegendLinkItemResult> Items);
