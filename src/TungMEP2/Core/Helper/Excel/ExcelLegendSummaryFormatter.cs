using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelLegendSummaryFormatter
{
	public static string Format(string title, ExcelLegendLinkSummary summary)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine(title);
		builder.AppendLine();
		builder.AppendLine($"Đã quét: {summary.Scanned}");
		builder.AppendLine($"Đã tạo: {summary.Created}");
		builder.AppendLine($"Đã cập nhật: {summary.Updated}");
		builder.AppendLine($"Không thay đổi: {summary.Unchanged}");
		builder.AppendLine($"Phát hiện cũ: {summary.StaleFound}");
		builder.AppendLine($"Mất liên kết: {summary.Orphaned}");
		builder.AppendLine($"Lỗi parse: {summary.ParseError}");
		builder.AppendLine($"Thất bại: {summary.Failed}");
		IReadOnlyList<string> warnings = (from item in summary.Items.Where((ExcelLegendLinkItemResult item) => item.IsWarning).Take(8)
			select "- " + item.Message).ToList();
		if (warnings.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("Cảnh báo:");
			foreach (string warning in warnings)
			{
				builder.AppendLine(warning);
			}
		}
		return builder.ToString().TrimEnd();
	}
}
