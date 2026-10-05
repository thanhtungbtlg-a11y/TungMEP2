using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal readonly struct DirectYDeletionCandidate
{
    internal DirectYDeletionCandidate(long id,bool existing,bool retained,bool disposable,string description)
    {Id=id;Existing=existing;Retained=retained;Disposable=disposable;Description=description;}
    internal long Id {get;}
    internal bool Existing {get;}
    internal bool Retained {get;}
    internal bool Disposable {get;}
    internal string Description {get;}
}

internal static class DirectYCleanupPolicy
{
    internal static bool IsDisposableCenterLine(bool existing,bool retained,bool recognizedNativeCenterLine,
        int temporaryOwnerCount,bool retainedDependent) => !existing && !retained &&
        recognizedNativeCenterLine && temporaryOwnerCount == 1 && !retainedDependent;

    internal static bool TryApprove(long stubId,IReadOnlyList<DirectYDeletionCandidate> deleted,out string error)
    {
        error = string.Empty;
        if (stubId <= 0 || deleted == null || deleted.Count == 0)
        { error = "Không có kết quả thử xóa Pipe tạm hợp lệ."; return false; }
        var seen = new HashSet<long>();
        foreach (var candidate in deleted)
        {
            string reason = candidate.Id <= 0 ? "ID không hợp lệ" :
                !seen.Add(candidate.Id) ? "ID lặp lại" :
                candidate.Existing ? "phần tử model có trước lệnh" :
                candidate.Retained ? "phần tử phải giữ" :
                !candidate.Disposable ? "chưa xác nhận là phần tử tạm an toàn" : null;
            if (reason != null)
            { error = $"Không cho phép xóa ID {candidate.Id} ({candidate.Description}): {reason}."; return false; }
        }
        if (!seen.Contains(stubId))
        { error = $"Kết quả thử xóa không chứa Pipe tạm ID {stubId}."; return false; }
        return true;
    }
    internal static bool TryVerifyActual(long stubId,IReadOnlyList<long> approved,IReadOnlyList<DirectYDeletionCandidate> actual,out string error)
    {
        if (!TryApprove(stubId, actual, out error)) return false;
        if (approved == null || approved.Count == 0 || approved.Distinct().Count() != approved.Count ||
            !new HashSet<long>(approved).SetEquals(actual.Select(candidate => candidate.Id)))
        {
            error = "Kết quả xóa thật khác lần thử đã duyệt. Đã duyệt: " +
                string.Join(",", approved ?? new long[0]) + "; xóa thật: " +
                string.Join(",", actual.Select(candidate => candidate.Id)) + ".";
            return false;
        }
        return true;
    }
}
