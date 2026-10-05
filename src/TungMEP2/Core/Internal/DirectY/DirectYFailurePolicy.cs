using System.Collections.Generic;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal sealed class DirectYFailurePolicy
{
    private readonly List<string> allowedWarnings = new List<string>();
    private readonly List<string> blockedFailures = new List<string>();
    internal IReadOnlyList<string> AllowedWarnings => allowedWarnings.AsReadOnly();
    internal string LastFailure => string.Join(" | ", blockedFailures);

    internal bool Record(bool isWarning, bool isMismatchedSystemType, string description)
    {
        string diagnostic = string.IsNullOrWhiteSpace(description)
            ? "Revit không cung cấp mô tả cho cảnh báo/lỗi DirectY."
            : description.Trim();
        if (isWarning && isMismatchedSystemType)
        {
            if (!allowedWarnings.Contains(diagnostic)) allowedWarnings.Add(diagnostic);
            return true;
        }
        if (!blockedFailures.Contains(diagnostic)) blockedFailures.Add(diagnostic);
        return false;
    }
}
