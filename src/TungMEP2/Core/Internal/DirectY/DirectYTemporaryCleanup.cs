using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYTemporaryCleanup
{
    private const double Tolerance = 0.5 / 304.8;

    internal static bool TryRemove(Document document, DirectYModelBaseline baseline, DirectYDiagnostics log,
        ElementId yId, ElementId mainId, ElementId splitId, ElementId sourceId, ElementId stubId, out string error)
    {
        error = string.Empty;
        var retained = new HashSet<long>(new[] { yId, mainId, splitId, sourceId }.Select(id => id.Value));
        if (!baseline.ElementsSurvive(document, out error) ||
            !TryCollectTemporaryBranch(document, baseline, yId, mainId, splitId, stubId,
                out HashSet<long> roots, out FitSpaceConnectorKey yBoundary, out FitSpaceConnectorKey tempBoundary, out error))
            return Fail(log, "cleanup/topology", error, out error);

        Connector yPort = FitSpaceConnectorService.FindByKey(document, yBoundary);
        Connector tempPort = FitSpaceConnectorService.FindByKey(document, tempBoundary);
        if (yPort == null || tempPort == null || !FitSpaceConnectorService.IsConnected(yPort, tempPort))
            return Fail(log, "cleanup/disconnect", "Không tìm lại được cặp connector Y–nhánh tạm.", out error);
        log.Write("cleanup/disconnect", $"Y={yId.Value}; stub={stubId.Value}; temporary roots={string.Join(",", roots)}");
        yPort.DisconnectFrom(tempPort);
        document.Regenerate();
        // Reacquire everything: disconnect/regeneration can invalidate native wrappers.
        if (!ValidateRetained(document, baseline, yId, mainId, splitId, sourceId, out error))
            return Fail(log, "cleanup/after-disconnect", error, out error);
        if (document.GetElement(stubId) is not Pipe)
            return Fail(log, "cleanup/after-disconnect", "Pipe tạm biến mất trước bước thử xóa; không xác nhận được dọn an toàn.", out error);
        roots.RemoveWhere(id => document.GetElement(new ElementId(id)) == null);

        long[] probed = null;
        using (var probe = new SubTransaction(document))
        {
            Start(probe, "thử xóa phần tử tạm");
            try
            {
                probed = document.Delete(roots.Select(id => new ElementId(id)).ToList()).Select(id => id.Value).ToArray();
                document.Regenerate();
            }
            finally { RollBack(probe, "thử xóa phần tử tạm"); }
        }

        // The delete probe is now rolled back. Classify the restored elements using native metadata,
        // not stale wrappers or the assumption that every newly created object is safe to delete.
        var dependencies = CaptureDependencies(document, log, roots, retained, probed);
        var approved = probed.Select(id => Classify(document, baseline, roots, retained, dependencies, id)).ToArray();
        foreach (var candidate in approved)
            log.Element("cleanup/delete-probe", document.GetElement(new ElementId(candidate.Id)),
                $"existing={candidate.Existing}; retained={candidate.Retained}; disposable={candidate.Disposable}; {candidate.Description}");
        if (!DirectYCleanupPolicy.TryApprove(stubId.Value, approved, out error))
            return Fail(log, "cleanup/delete-probe-rejected", error, out error);
        if (!ValidateRetained(document, baseline, yId, mainId, splitId, sourceId, out error))
            return Fail(log, "cleanup/probe-rollback-validation", error, out error);

        var approvedById = approved.ToDictionary(candidate => candidate.Id);
        using (var deletion = new SubTransaction(document))
        {
            Start(deletion, "xóa phần tử tạm đã duyệt");
            try
            {
                long[] actualIds = document.Delete(roots.Select(id => new ElementId(id)).ToList()).Select(id => id.Value).ToArray();
                var actual = actualIds.Select(id => approvedById.TryGetValue(id, out var known) ? known :
                    new DirectYDeletionCandidate(id, baseline.AllIds.Contains(id), retained.Contains(id), false,
                        "ID phát sinh ngoài kết quả thử đã duyệt")).ToArray();
                log.Write("cleanup/delete-actual", "IDs=" + string.Join(",", actualIds));
                if (!DirectYCleanupPolicy.TryVerifyActual(stubId.Value, probed, actual, out error))
                    return Fail(log, "cleanup/delete-actual-rejected", error, out error);
                document.Regenerate();
                if (!ValidateRetained(document, baseline, yId, mainId, splitId, sourceId, out error))
                    return Fail(log, "cleanup/after-delete", error, out error);
                if (deletion.Commit() != TransactionStatus.Committed)
                    return Fail(log, "cleanup/delete-commit", "Không xác nhận được commit bước dọn phần tử tạm.", out error);
                log.Write("cleanup/accepted", "Đã ngắt khỏi Y, thử xóa/rollback, phân loại và xóa đúng tập ID đã duyệt.");
                return true;
            }
            finally { RollBack(deletion, "xóa phần tử tạm đã duyệt"); }
        }
    }

    private static bool TryCollectTemporaryBranch(Document document, DirectYModelBaseline baseline,
        ElementId yId, ElementId mainId, ElementId splitId, ElementId stubId, out HashSet<long> roots,
        out FitSpaceConnectorKey yBoundary, out FitSpaceConnectorKey tempBoundary, out string error)
    {
        roots = new HashSet<long> { stubId.Value }; yBoundary = tempBoundary = null;
        error = "Y phải nối trực tiếp hai Pipe chính và chỉ có một nhánh tới Pipe tạm.";
        var ports = DirectYTopology.Ports(document.GetElement(yId));
        if (ports.Count != 3 || !HasMainRun(ports, mainId, splitId)) return false;
        Connector branch = ports.SingleOrDefault(port => DirectYTopology.SinglePeer(port, out var peer) &&
            peer.Owner.Id != mainId && peer.Owner.Id != splitId);
        if (branch == null || !DirectYTopology.SinglePeer(branch, out var incoming)) return false;
        yBoundary = FitSpaceConnectorService.CreateKey(branch);
        tempBoundary = FitSpaceConnectorService.CreateKey(incoming);
        for (int count = 0; count < 8; count++)
        {
            Element element = incoming.Owner;
            if (element.Id == stubId)
            {
                var stubPorts = DirectYTopology.Ports(element);
                if (stubPorts.Count != 2 || stubPorts.Any(port => port.Id != incoming.Id && port.IsConnected))
                { error = "Đầu xa Pipe tạm đã nối tới phần tử khác; không xóa."; return false; }
                error = string.Empty; return true;
            }
            if (baseline.AllIds.Contains(element.Id.Value) || !roots.Add(element.Id.Value) ||
                element is not FamilyInstance fitting || fitting.MEPModel is not MechanicalFitting mechanical ||
                mechanical.PartType != PartType.Transition ||
                fitting.Category?.Id.Value != (long)BuiltInCategory.OST_PipeFitting)
            { error = $"Nhánh tạm gặp ID {element.Id.Value} không phải transition mới được tạo; không xóa."; return false; }
            var transitionPorts = DirectYTopology.Ports(element);
            if (transitionPorts.Count != 2 || transitionPorts.Any(port =>
                !DirectYRunner.HasOnlySupportedEndReferences(port) ||
                FitSpaceConnectorService.GetConnectedPhysicalReferences(port).Count != 1))
            { error = $"Transition tạm ID {element.Id.Value} có kết nối phụ/không rõ; không xóa."; return false; }
            Connector outgoing = transitionPorts.Single(port => port.Id != incoming.Id);
            if (!DirectYTopology.SinglePeer(outgoing, out incoming)) return false;
        }
        error = "Chuỗi transition tạm quá dài hoặc tạo vòng; không xóa."; return false;
    }

    private static DirectYDeletionCandidate Classify(Document document, DirectYModelBaseline baseline,
        HashSet<long> roots, HashSet<long> retained, Dictionary<long, HashSet<long>> dependencies, long id)
    {
        Element element = document.GetElement(new ElementId(id));
        bool existing = baseline.AllIds.Contains(id), keep = retained.Contains(id), disposable = false;
        string reason = element?.GetType().FullName ?? "Không tìm lại được phần tử sau rollback";
        if (!existing && !keep && element != null)
        {
            if (roots.Contains(id)) { disposable = true; reason += "; thuộc chuỗi nhánh tạm đã xác nhận"; }
            else if (element is PipeInsulation insulation && roots.Contains(insulation.HostElementId.Value))
            { disposable = true; reason += "; insulation mới của phần tử tạm"; }
            else if (IsNativeCenterLine(element))
            {
                long category = element.Category.Id.Value;
                long[] temporaryOwners = roots.Where(owner => dependencies[owner].Contains(id) &&
                    CenterLineMatchesOwner(document.GetElement(new ElementId(owner)), category)).ToArray();
                long[] retainedOwners = retained.Where(owner => dependencies[owner].Contains(id)).ToArray();
                disposable = DirectYCleanupPolicy.IsDisposableCenterLine(existing, keep, true,
                    temporaryOwners.Length, retainedOwners.Length > 0);
                reason += "; Center Line; temporaryOwners=" + string.Join(",", temporaryOwners) +
                    "; retainedOwners=" + string.Join(",", retainedOwners);
            }
            else if (element is PipingSystem system)
            {
                disposable = IsTemporaryOnlySystem(document, system, roots);
                reason += disposable ? "; system mới chỉ chứa nhánh tạm/đã rỗng" : "; system chưa chứng minh chỉ thuộc nhánh tạm";
            }
        }
        return new DirectYDeletionCandidate(id, existing, keep, disposable, reason);
    }

    private static Dictionary<long, HashSet<long>> CaptureDependencies(Document document, DirectYDiagnostics log,
        HashSet<long> roots, HashSet<long> retained, long[] candidateIds)
    {
        var candidates = new HashSet<long>(candidateIds);
        var result = new Dictionary<long, HashSet<long>>();
        // Read only after the delete probe has confirmed rollback. Never infer ownership from
        // ID adjacency/category name or treat a failed native dependency query as an empty set.
        foreach (long ownerId in roots.Concat(retained).Distinct())
        {
            Element owner = document.GetElement(new ElementId(ownerId));
            if (owner == null) throw new InvalidOperationException("cleanup/dependency-proof: Không tìm lại được owner ID " + ownerId + ".");
            log.Write("cleanup/dependency-proof", "Query owner=" + ownerId);
            var children = new HashSet<long>(owner.GetDependentElements(null).Select(child => child.Value));
            result.Add(ownerId, children);
            log.Write("cleanup/dependency-proof", $"owner={ownerId}; temporaryRoot={roots.Contains(ownerId)}; candidateChildren={string.Join(",", children.Where(candidates.Contains))}");
        }
        return result;
    }

    private static bool IsNativeCenterLine(Element element) => element.GetType() == typeof(Element) &&
        element.GetTypeId() == ElementId.InvalidElementId &&
        (element.Category?.Id.Value == (long)BuiltInCategory.OST_PipeCurvesCenterLine ||
         element.Category?.Id.Value == (long)BuiltInCategory.OST_PipeFittingCenterLine);

    private static bool CenterLineMatchesOwner(Element owner, long category) =>
        (category == (long)BuiltInCategory.OST_PipeCurvesCenterLine && owner is Pipe) ||
        (category == (long)BuiltInCategory.OST_PipeFittingCenterLine && owner is FamilyInstance fitting &&
            fitting.MEPModel is MechanicalFitting mechanical && mechanical.PartType == PartType.Transition);

    private static bool IsTemporaryOnlySystem(Document document, PipingSystem system, HashSet<long> roots)
    {
        if (system.BaseEquipment != null || system.PipingNetwork.Cast<Element>().Any(element => !roots.Contains(element.Id.Value)) ||
            system.Elements.Cast<Element>().Any(element => !roots.Contains(element.Id.Value))) return false;
        // Elements is only terminals, so inspect PipingNetwork above AND connector memberships.
        // A new, empty-looking managed system must not still be used by a retained pipe/fitting.
        foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            if (roots.Contains(element.Id.Value) || (element is not MEPCurve && element is not FamilyInstance)) continue;
            if (DirectYTopology.Ports(element).Any(port => port.MEPSystem?.Id == system.Id)) return false;
        }
        return true;
    }

    private static bool ValidateRetained(Document document, DirectYModelBaseline baseline, ElementId yId,
        ElementId mainId, ElementId splitId, ElementId sourceId, out string error)
    {
        if (!baseline.ElementsSurvive(document, out error)) return false;
        error = "Dọn nhánh tạm làm mất Y/Pipe chính/Pipe nhánh hoặc đổi kết nối Y–Pipe chính; rollback.";
        if (document.GetElement(yId) is not FamilyInstance || document.GetElement(mainId) is not Pipe ||
            document.GetElement(splitId) is not Pipe || document.GetElement(sourceId) is not Pipe) return false;
        var ports = DirectYTopology.Ports(document.GetElement(yId));
        if (ports.Count != 3 || ports.Count(port => !port.IsConnected) != 1 || !HasMainRun(ports, mainId, splitId)) return false;
        error = string.Empty; return true;
    }

    private static bool HasMainRun(IReadOnlyList<Connector> ports, ElementId mainId, ElementId splitId)
    {
        var matched = new HashSet<long>();
        foreach (Connector port in ports)
        {
            if (!DirectYTopology.SinglePeer(port, out var peer) || (peer.Owner.Id != mainId && peer.Owner.Id != splitId)) continue;
            if (!DirectYTopology.SinglePeer(peer, out var back) || back.Owner.Id != port.Owner.Id || back.Id != port.Id ||
                port.Origin.DistanceTo(peer.Origin) > Tolerance ||
                port.CoordinateSystem.BasisZ.Normalize().DotProduct(peer.CoordinateSystem.BasisZ.Normalize()) > -Math.Cos(2 * Math.PI / 180))
                return false;
            matched.Add(peer.Owner.Id.Value);
        }
        return matched.Count == 2;
    }

    private static void Start(SubTransaction transaction, string stage)
    {
        if (transaction.Start() != TransactionStatus.Started) throw new DirectYAbortException("Không mở được " + stage + ".");
    }
    private static void RollBack(SubTransaction transaction, string stage)
    {
        if (transaction.GetStatus() == TransactionStatus.Started && transaction.RollBack() != TransactionStatus.RolledBack)
            throw new DirectYAbortException("Không xác nhận được rollback " + stage + ".");
    }
    private static bool Fail(DirectYDiagnostics log, string stage, string message, out string error)
    { error = stage + ": " + message; log.Write(stage, message); return false; }
}
