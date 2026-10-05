using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class CropLineWeightReport
{
	public int TotalViews { get; set; }

	public int SuccessCount { get; set; }

	public int LineWeight { get; set; }

	public List<string> FailedViews { get; } = new List<string>();

	public string ToDisplayText()
	{
		int failedCount = FailedViews.Count;
		if (failedCount == 0)
		{
			return $"Lineweight: {LineWeight}\nApplied: {SuccessCount}/{TotalViews}\nKhông có lỗi.";
		}
		return $"Lineweight: {LineWeight}\nApplied: {SuccessCount}/{TotalViews}\nFailed: {failedCount}\n\n" + string.Join(Environment.NewLine, FailedViews);
	}
}
