using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

// Captured before any native Y insertion/probe. Never replace this with a post-insertion snapshot.
internal sealed class DirectYInnerClusterBaseline
{
    private const double PositionTolerance = 0.5 / 304.8;
    private const double UnchangedTolerance = 0.1 / 304.8;
    private const double AngularTolerance = 0.5 * Math.PI / 180.0;

    private sealed class PortSnapshot
    {
        internal FitSpaceConnectorKey Key;
        internal FitSpaceConnectorKey Peer;
    }

    private sealed class ElementSnapshot
    {
        internal ElementId Id, TypeId;
        internal string UniqueId;
        internal PortSnapshot[] Ports;
    }

    private readonly HashSet<long> innerIds;
    private readonly ElementSnapshot[] elements;
    private readonly FitSpaceConnectorBinding[] internalBindings;
    private readonly FitSpaceConnectorKey sourceOpen;

    private DirectYInnerClusterBaseline(HashSet<long> innerIds, ElementSnapshot[] elements,
        FitSpaceConnectorBinding[] internalBindings, FitSpaceConnectorKey sourceOpen)
    {
        this.innerIds = innerIds;
        this.elements = elements;
        this.internalBindings = internalBindings;
        this.sourceOpen = sourceOpen;
    }

    internal static bool TryCapture(Document document, Pipe source,
        out DirectYInnerClusterBaseline baseline, out string error)
    {
        baseline = null; error = string.Empty;
        if (document.IsModifiable)
        { error = "Phải ghi nhận cụm nhánh trước khi mở transaction tạo Y."; return false; }
        var ports = DirectYTopology.Ports(source);
        if (ports.Count != 2 || ports.Count(port => !port.IsConnected) != 1)
        { error = "Không ghi nhận được đầu hở ban đầu của Pipe nhánh."; return false; }
        Connector open = ports.Single(port => !port.IsConnected);
        Connector near = ports.Single(port => port.Id != open.Id);
        if (!DirectYTopology.TryCollectTail(document, source, near, out var tail, out error)) return false;
        var inner = new HashSet<long>(tail.MovingIds.Select(id => id.Value));
        var context = new HashSet<ElementId>(tail.MovingIds) { tail.Anchor.Id };
        Connector anchorFar = DirectYTopology.Ports(tail.Anchor).Single(port => port.Id != tail.AnchorNear.Id);
        Connector fixedFar = anchorFar;
        if (DirectYTopology.SinglePeer(anchorFar, out var peer))
        {
            context.Add(peer.Owner.Id);
            if (DirectYTopology.IsElbow(peer.Owner, out var outerElbow))
            {
                Connector other = DirectYTopology.Ports(outerElbow).Single(port => port.Id != peer.Id);
                if (DirectYTopology.SinglePeer(other, out var upstreamNear) && upstreamNear.Owner is Pipe upstream)
                {
                    context.Add(upstream.Id);
                    fixedFar = DirectYTopology.Ports(upstream).Single(port => port.Id != upstreamNear.Id);
                }
            }
        }
        // Context geometry is protected before motion, but is NEVER granted an offset exemption.
        if (DirectYTopology.SinglePeer(fixedFar, out var fixedPeer)) context.Add(fixedPeer.Owner.Id);
        var snapshots = new List<ElementSnapshot>();
        foreach (ElementId id in context)
        {
            Element element = document.GetElement(id);
            if (element == null || !DirectYRunner.HasOnlyEndPorts(element))
            { error = "Không ghi nhận được đầy đủ connector ban đầu của cụm nhánh."; return false; }
            var capturedPorts = new List<PortSnapshot>();
            foreach (Connector port in DirectYTopology.Ports(element))
            {
                var references = FitSpaceConnectorService.GetConnectedPhysicalReferences(port);
                if (!DirectYRunner.HasOnlySupportedEndReferences(port) || references.Count > 1 ||
                    (references.Count == 1 && !IsReciprocalUnique(port, references[0])))
                { error = "Kết nối ban đầu không phải cặp End hai chiều duy nhất."; return false; }
                var key = FitSpaceConnectorService.CreateKey(port);
                if (!MatchesOriginal(port, key))
                { error = "Hình học connector ban đầu không hợp lệ."; return false; }
                capturedPorts.Add(new PortSnapshot
                {
                    Key = key,
                    Peer = references.Count == 1 ? FitSpaceConnectorService.CreateKey(references[0]) : null
                });
            }
            snapshots.Add(new ElementSnapshot
            {
                Id = id, UniqueId = element.UniqueId, TypeId = element.GetTypeId(), Ports = capturedPorts.ToArray()
            });
        }
        baseline = new DirectYInnerClusterBaseline(inner, snapshots.ToArray(), tail.Bindings.ToArray(),
            FitSpaceConnectorService.CreateKey(open));
        return baseline.ValidateUnmoved(document, tail.MovingIds, tail.Bindings,
            FitSpaceConnectorService.CreateKey(open), out error);
    }

    internal bool ValidateUnmoved(Document document, IEnumerable<ElementId> currentInnerIds,
        IEnumerable<FitSpaceConnectorBinding> currentInternalBindings, FitSpaceConnectorKey currentSourceOpen,
        out string error)
    {
        error = string.Empty;
        var currentIds = currentInnerIds.Select(id => id.Value).ToArray();
        var bindings = currentInternalBindings.ToArray();
        if (currentIds.Length != innerIds.Count || !innerIds.SetEquals(currentIds) ||
            !SamePort(sourceOpen, currentSourceOpen) || bindings.Length != internalBindings.Length ||
            bindings.Select(BindingIdentity).Distinct().Count() != bindings.Length ||
            !new HashSet<string>(internalBindings.Select(BindingIdentity)).SetEquals(bindings.Select(BindingIdentity)))
        { error = "Phạm vi/định danh cụm nhánh đã thay đổi sau khi ghi nhận ban đầu; rollback."; return false; }
        foreach (ElementSnapshot snapshot in elements)
        {
            if (!ValidateIdentityAndPorts(document, snapshot, out error)) return false;
            foreach (PortSnapshot port in snapshot.Ports)
            {
                Connector current = Find(document, port.Key);
                if (!MatchesOriginal(current, port.Key) || !MatchesOriginalPeer(document, current, port.Peer))
                { error = "Hình học hoặc kết nối nhánh đã thay đổi trước bước xoay; rollback."; return false; }
            }
        }
        return true;
    }

    internal bool AllowsExistingOffset(Document document, FitSpaceConnectorBinding binding)
    {
        FitSpaceConnectorBinding original = internalBindings.FirstOrDefault(value => SameBinding(value, binding));
        if (original == null || !innerIds.Contains(binding.First.OwnerId.Value) ||
            !innerIds.Contains(binding.Second.OwnerId.Value)) return false;
        Connector first = Find(document, original.First), second = Find(document, original.Second);
        return DirectYInternalBindingGeometry.AllowsExistingOffset(
            true, MatchesOriginal(first, original.First) && MatchesOriginal(second, original.Second),
            IsReciprocalUnique(first, second), MatchedRoundSize(first, second),
            original.First.OriginalOrigin.DistanceTo(original.Second.OriginalOrigin),
            V(original.First.OriginalBasisZ).Dot(V(original.Second.OriginalBasisZ)) /
                (V(original.First.OriginalBasisZ).Length * V(original.Second.OriginalBasisZ).Length), PositionTolerance);
    }

    internal bool IsOriginalInternal(FitSpaceConnectorBinding binding) =>
        innerIds.Contains(binding.First.OwnerId.Value) && innerIds.Contains(binding.Second.OwnerId.Value) &&
        internalBindings.Any(original => SameBinding(original, binding));

    internal bool ValidateTransformed(Document document, DirectYRigidStep? outer, DirectYRigidStep inner,
        out string error)
    {
        error = string.Empty;
        foreach (ElementSnapshot snapshot in elements.Where(element => innerIds.Contains(element.Id.Value)))
        {
            if (!ValidateIdentityAndPorts(document, snapshot, out error)) return false;
            foreach (PortSnapshot port in snapshot.Ports)
            {
                Connector current = Find(document, port.Key);
                if (current == null || !MatchesDimensions(current, port.Key) ||
                    !DirectYRunner.HasOnlySupportedEndReferences(current) ||
                    !TryExpected(port.Key, outer, inner, out var point, out var axis) ||
                    !DirectYInternalBindingGeometry.MatchesPort(
                        V(current.Origin), V(current.CoordinateSystem?.BasisZ),
                        SamePort(port.Key, sourceOpen) ? V(current.Origin) : point, axis,
                        UnchangedTolerance, AngularTolerance) ||
                    (current.Owner is FamilyInstance && !MatchesTransformedFittingFrame(current, port.Key, outer, inner)) ||
                    (!SamePort(port.Key, sourceOpen) && !MatchesOriginalPeer(document, current, port.Peer)))
                {
                    error = $"Connector {port.Key.OwnerId.Value}:{port.Key.ConnectorId} không giữ đúng phép xoay/tịnh tiến của cụm; rollback.";
                    return false;
                }
            }
        }
        // Compare signed 3D offsets, not just distances (equal gap with a different azimuth is NOT preserved).
        foreach (FitSpaceConnectorBinding binding in internalBindings)
        {
            Connector first = Find(document, binding.First), second = Find(document, binding.Second);
            if (!IsReciprocalUnique(first, second) ||
                !TryExpected(binding.First, outer, inner, out var expectedFirst, out _) ||
                !TryExpected(binding.Second, outer, inner, out var expectedSecond, out _) ||
                !DirectYInternalBindingGeometry.MatchesOffset(V(first.Origin), V(second.Origin),
                    expectedFirst, expectedSecond, UnchangedTolerance))
            { error = "Vector lệch của kết nối bên trong cụm đã thay đổi; rollback."; return false; }
        }
        return true;
    }

    internal static bool IsReciprocalUnique(Connector first, Connector second) =>
        first != null && second != null && FitSpaceConnectorService.IsConnected(first, second) &&
        FitSpaceConnectorService.IsConnected(second, first) &&
        FitSpaceConnectorService.GetConnectedPhysicalReferences(first).Count == 1 &&
        FitSpaceConnectorService.GetConnectedPhysicalReferences(second).Count == 1;

    private static bool ValidateIdentityAndPorts(Document document, ElementSnapshot snapshot, out string error)
    {
        error = string.Empty;
        Element current = document.GetElement(snapshot.Id);
        if (current == null || current.UniqueId != snapshot.UniqueId || current.GetTypeId() != snapshot.TypeId ||
            !DirectYRunner.HasOnlyEndPorts(current))
        { error = "ID/loại hoặc connector của cụm nhánh ban đầu đã thay đổi; rollback."; return false; }
        var ids = DirectYTopology.Ports(current).Select(port => port.Id).ToArray();
        if (ids.Length != snapshot.Ports.Length ||
            !new HashSet<int>(snapshot.Ports.Select(port => port.Key.ConnectorId)).SetEquals(ids))
        { error = "Tập connector trong cụm nhánh ban đầu đã thay đổi; rollback."; return false; }
        return true;
    }

    private static bool MatchesOriginal(Connector current, FitSpaceConnectorKey key) =>
        current != null && MatchesDimensions(current, key) &&
        DirectYInternalBindingGeometry.MatchesPort(V(current.Origin), V(current.CoordinateSystem?.BasisZ),
            V(key.OriginalOrigin), V(key.OriginalBasisZ), UnchangedTolerance, AngularTolerance) &&
        DirectYInternalBindingGeometry.MatchesPort(V(current.Origin), V(current.CoordinateSystem?.BasisX),
            V(key.OriginalOrigin), V(key.OriginalBasisX), UnchangedTolerance, AngularTolerance) &&
        DirectYInternalBindingGeometry.MatchesPort(V(current.Origin), V(current.CoordinateSystem?.BasisY),
            V(key.OriginalOrigin), V(key.OriginalBasisY), UnchangedTolerance, AngularTolerance);

    private static bool MatchesDimensions(Connector current, FitSpaceConnectorKey key) =>
        current.Shape == key.OriginalShape && (current.Shape switch
        {
            ConnectorProfileType.Round => Math.Abs(current.Radius - key.OriginalRadius) <= UnchangedTolerance,
            ConnectorProfileType.Rectangular or ConnectorProfileType.Oval =>
                Math.Abs(current.Width - key.OriginalWidth) <= UnchangedTolerance &&
                Math.Abs(current.Height - key.OriginalHeight) <= UnchangedTolerance,
            _ => false
        });

    private static bool MatchesOriginalPeer(Document document, Connector current, FitSpaceConnectorKey peer) =>
        peer == null ? current != null && !current.IsConnected &&
            FitSpaceConnectorService.GetConnectedPhysicalReferences(current).Count == 0 :
            IsReciprocalUnique(current, Find(document, peer));

    private static bool MatchedRoundSize(Connector first, Connector second) =>
        first != null && second != null && first.Shape == ConnectorProfileType.Round &&
        second.Shape == ConnectorProfileType.Round && Math.Abs(first.Radius - second.Radius) <= UnchangedTolerance;

    private static bool TryExpected(FitSpaceConnectorKey key, DirectYRigidStep? outer, DirectYRigidStep inner,
        out Vector3d point, out Vector3d axis) => DirectYInternalBindingGeometry.TryTransform(
            V(key.OriginalOrigin), V(key.OriginalBasisZ), outer, inner, out point, out axis);
    private static bool MatchesTransformedFittingFrame(Connector current, FitSpaceConnectorKey key,
        DirectYRigidStep? outer, DirectYRigidStep inner) =>
        DirectYInternalBindingGeometry.TryTransform(V(key.OriginalOrigin), V(key.OriginalBasisX), outer, inner, out _, out var expectedX) &&
        DirectYInternalBindingGeometry.TryTransform(V(key.OriginalOrigin), V(key.OriginalBasisY), outer, inner, out _, out var expectedY) &&
        DirectYInternalBindingGeometry.MatchesFittingFrame(V(current.CoordinateSystem?.BasisX),
            V(current.CoordinateSystem?.BasisY), expectedX, expectedY, AngularTolerance);
    private static bool SameBinding(FitSpaceConnectorBinding first, FitSpaceConnectorBinding second) =>
        BindingIdentity(first) == BindingIdentity(second);
    private static string BindingIdentity(FitSpaceConnectorBinding binding)
    {
        string first = PortIdentity(binding.First), second = PortIdentity(binding.Second);
        return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
    }
    private static string PortIdentity(FitSpaceConnectorKey key) => key.OwnerUniqueId + ":" + key.ConnectorId;
    private static bool SamePort(FitSpaceConnectorKey first, FitSpaceConnectorKey second) =>
        first != null && second != null && first.OwnerId == second.OwnerId &&
        first.OwnerUniqueId == second.OwnerUniqueId && first.ConnectorId == second.ConnectorId;
    private static Connector Find(Document document, FitSpaceConnectorKey key) => FitSpaceConnectorService.FindByKey(document, key);
    private static Vector3d V(XYZ value) => value == null ? new Vector3d(double.NaN, double.NaN, double.NaN) :
        new Vector3d(value.X, value.Y, value.Z);
}
