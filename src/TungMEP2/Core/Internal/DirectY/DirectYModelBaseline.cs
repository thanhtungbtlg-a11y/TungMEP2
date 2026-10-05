using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal sealed class DirectYModelBaseline
{
    internal readonly HashSet<long> AllIds;
    private readonly HashSet<long> ManagedSystemIds;

    internal DirectYModelBaseline(Document document)
    {
        AllIds = CaptureAllIds(document);
        ManagedSystemIds = new HashSet<long>(new FilteredElementCollector(document).OfClass(typeof(MEPSystem))
            .ToElementIds().Select(id => id.Value));
    }

    internal bool ElementsSurvive(Document document, out string error)
    {
        var current = CaptureAllIds(document);
        // Native joining can merge managed MEPSystem records. No pre-existing record is ever approved
        // for explicit deletion by cleanup; physical/model elements must survive every native operation.
        long[] lost = AllIds.Where(id => !ManagedSystemIds.Contains(id) && !current.Contains(id)).Take(20).ToArray();
        error = lost.Length == 0 ? string.Empty : "Phần tử model có trước lệnh bị mất: " + string.Join(",", lost) + ".";
        return lost.Length == 0;
    }

    private static HashSet<long> CaptureAllIds(Document document)
    {
        // Revit requires a native filter before extraction. Use complementary, separately
        // filtered collectors so protecting all existing IDs still includes ElementTypes.
        using var instances = new FilteredElementCollector(document).WhereElementIsNotElementType();
        using var types = new FilteredElementCollector(document).WhereElementIsElementType();
        var ids = new HashSet<long>(instances.ToElementIds().Select(id => id.Value));
        ids.UnionWith(types.ToElementIds().Select(id => id.Value));
        return ids;
    }
}
