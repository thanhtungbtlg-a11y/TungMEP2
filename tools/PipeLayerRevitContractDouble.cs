// Narrow out-of-host boundary for the real PipeLayer runner/safety code.
// Revit elements cannot be constructed outside a native document. The fixture
// supplies observed geometry/topology and commit-time changes; it does NOT solve
// fittings or prove native transaction, worksharing, hosting, UI or Undo behavior.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Autodesk.Revit.DB
{
    public sealed class ElementId : IEquatable<ElementId>
    {
        public static readonly ElementId InvalidElementId = new ElementId(-1);
        public ElementId(long value) { Value = value; }
        public long Value { get; }
        public bool Equals(ElementId other) => other != null && Value == other.Value;
        public override bool Equals(object other) => Equals(other as ElementId);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ElementId a, ElementId b) => ReferenceEquals(a, b) || (a is not null && a.Equals(b));
        public static bool operator !=(ElementId a, ElementId b) => !(a == b);
    }
    public sealed class XYZ
    {
        public XYZ(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; } public double Y { get; } public double Z { get; }
        public static XYZ BasisX => new XYZ(1, 0, 0);
        public static XYZ BasisY => new XYZ(0, 1, 0);
        public static XYZ BasisZ => new XYZ(0, 0, 1);
        public XYZ Add(XYZ p) => new XYZ(X + p.X, Y + p.Y, Z + p.Z);
        public XYZ Subtract(XYZ p) => new XYZ(X - p.X, Y - p.Y, Z - p.Z);
        public XYZ Multiply(double v) => new XYZ(X * v, Y * v, Z * v);
        public double DotProduct(XYZ p) => X * p.X + Y * p.Y + Z * p.Z;
        public double GetLength() => Math.Sqrt(DotProduct(this));
        public XYZ Normalize() => Multiply(1 / GetLength());
        public double DistanceTo(XYZ p) => Subtract(p).GetLength();
        public bool IsAlmostEqualTo(XYZ p) => p != null && DistanceTo(p) <= 1E-9;
    }
    public sealed class Transform
    {
        public XYZ Origin { get; set; } = new XYZ(0, 0, 0);
        public XYZ BasisX { get; set; } = XYZ.BasisX;
        public XYZ BasisY { get; set; } = XYZ.BasisY;
        public XYZ BasisZ { get; set; } = XYZ.BasisZ;
        public Transform Copy() => new Transform { Origin = Origin, BasisX = BasisX, BasisY = BasisY, BasisZ = BasisZ };
    }
    public class Curve { }
    public sealed class Line : Curve
    {
        private readonly XYZ start, end;
        private Line(XYZ start, XYZ end) { this.start = start; this.end = end; }
        public static Line CreateBound(XYZ start, XYZ end) => new Line(start, end);
        public XYZ GetEndPoint(int index) => index == 0 ? start : end;
        public double Length => start.DistanceTo(end);
    }
    public abstract class Location { }
    public sealed class LocationCurve : Location { public Curve Curve { get; set; } }
    public enum BuiltInParameter { RBS_PIPE_OUTER_DIAMETER, RBS_PIPE_DIAMETER_PARAM, RBS_PIPE_SLOPE, RBS_PIPING_SYSTEM_TYPE_PARAM }
    public enum BuiltInCategory { OST_PipeCurves = -2008044, OST_PipeFitting = -2008049, OST_PipeAccessory = -2008055, OST_MechanicalEquipment = -2001140, OST_GenericModel = -2000151 }
    public enum StorageType { Double, ElementId }
    public sealed class Parameter
    {
        public object Value { get; set; }
        public StorageType StorageType => Value is ElementId ? StorageType.ElementId : StorageType.Double;
        public double AsDouble() => (double)Value;
        public ElementId AsElementId() => (ElementId)Value;
    }
    public sealed class Category { public ElementId Id { get; set; } }
    public sealed class DesignOption { public ElementId Id { get; set; } }
    public class Element
    {
        public ElementId Id { get; set; }
        public string UniqueId { get; set; }
        public ElementId TypeId { get; set; } = new ElementId(10);
        public bool Pinned { get; set; }
        public ElementId GroupId { get; set; } = ElementId.InvalidElementId;
        public DesignOption DesignOption { get; set; }
        public Category Category { get; set; }
        public Location Location { get; set; }
        public Document Document { get; internal set; }
        public readonly Dictionary<BuiltInParameter, Parameter> Parameters = new Dictionary<BuiltInParameter, Parameter>();
        public readonly List<ElementId> Dependents = new List<ElementId>();
        public ElementId GetTypeId() => TypeId;
        public Parameter get_Parameter(BuiltInParameter key) => Parameters.TryGetValue(key, out Parameter value) ? value : null;
        public ICollection<ElementId> GetDependentElements(ElementFilter filter) => Dependents.Where(id => filter == null || filter.Allows(Document.GetElement(id))).ToArray();
    }
    public class MEPCurve : Element { public ConnectorManager ConnectorManager { get; } = new ConnectorManager(); }
    public sealed class FamilyInstance : Element
    {
        public MEPModel MEPModel { get; set; }
        public Element Host { get; set; }
        public Reference HostFace { get; set; }
        public FamilyInstance SuperComponent { get; set; }
        public Transform Placement { get; set; } = new Transform();
        public Transform GetTransform() => Placement.Copy();
    }
    public sealed class MEPModel { public ConnectorManager ConnectorManager { get; } = new ConnectorManager(); }
    public sealed class ConnectorManager { public ConnectorSet Connectors { get; } = new ConnectorSet(); }
    public sealed class ConnectorSet : List<Connector> { }
    public enum Domain { DomainPiping, DomainElectrical }
    public enum ConnectorType { End, Curve, Physical, Logical }
    public enum ConnectorProfileType { Invalid, Round, Rectangular, Oval }
    public sealed class MEPSystem { public ElementId Id { get; set; } = new ElementId(20); }
    public sealed class Connector
    {
        public Element Owner { get; set; }
        public int Id { get; set; }
        public Domain Domain { get; set; } = Domain.DomainPiping;
        public ConnectorType ConnectorType { get; set; } = ConnectorType.End;
        public XYZ Origin { get; set; }
        public Transform CoordinateSystem { get; set; } = new Transform();
        public ConnectorProfileType Shape { get; set; } = ConnectorProfileType.Round;
        public double Radius { get; set; } = 0.1;
        public double Width { get; set; } public double Height { get; set; }
        public MEPSystem MEPSystem { get; set; } = new MEPSystem();
        public ConnectorSet AllRefs { get; } = new ConnectorSet();
        public bool IsConnected => AllRefs.Any(p => p.Owner.Id != Owner.Id && p.ConnectorType != ConnectorType.Logical);
        public bool IsConnectedTo(Connector peer) => AllRefs.Contains(peer);
        public void ConnectTo(Connector peer) { if (!AllRefs.Contains(peer)) AllRefs.Add(peer); if (!peer.AllRefs.Contains(this)) peer.AllRefs.Add(this); }
        public void DisconnectFrom(Connector peer) { AllRefs.Remove(peer); peer.AllRefs.Remove(this); }
    }
    public sealed class Reference { public ElementId ElementId { get; set; } public XYZ GlobalPoint { get; set; } }
    public abstract class ElementFilter { internal abstract bool Allows(Element element); }
    public sealed class ElementClassFilter : ElementFilter
    {
        private readonly Type type;
        public ElementClassFilter(Type type) { this.type = type; }
        internal override bool Allows(Element element) => element != null && type.IsInstanceOfType(element);
    }
    public sealed class FilteredElementCollector : IEnumerable<Element>, IDisposable
    {
        private IEnumerable<Element> elements; private bool filtered;
        public FilteredElementCollector(Document document) { elements = document.Elements.Values; }
        public FilteredElementCollector OfClass(Type type) { elements = elements.Where(type.IsInstanceOfType); filtered = true; return this; }
        public FilteredElementCollector WhereElementIsNotElementType() { filtered = true; return this; }
        public IEnumerator<Element> GetEnumerator() { if (!filtered) throw new InvalidOperationException("Collector requires a filter"); return elements.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() { }
    }
    public enum CheckoutStatus { NotOwned, OwnedByOtherUser }
    public static class WorksharingUtils { public static CheckoutStatus GetCheckoutStatus(Document doc, ElementId id) => doc.OtherOwned.Contains(id.Value) ? CheckoutStatus.OwnedByOtherUser : CheckoutStatus.NotOwned; }
    public static class UnitTypeId { public static object Millimeters => null; }
    public static class UnitUtils { public static double ConvertToInternalUnits(double mm, object unit) => mm / 304.8; }
    public sealed class Document
    {
        public bool IsReadOnly { get; set; } public bool IsWorkshared { get; set; }
        public readonly Dictionary<long, Element> Elements = new Dictionary<long, Element>();
        public readonly HashSet<long> OtherOwned = new HashSet<long>();
        public Action<ElementId, XYZ> MoveEffect;
        public Action CommitEffect;
        public readonly FailuresAccessor PostedFailures = new FailuresAccessor();
        public TransactionStatus CommitResult = TransactionStatus.Committed;
        public TransactionStatus AssimilateResult = TransactionStatus.Committed;
        public bool GroupRolledBack;
        public void Add(Element e) { e.Document = this; Elements.Add(e.Id.Value, e); }
        public Element GetElement(ElementId id) => id != null && Elements.TryGetValue(id.Value, out Element e) ? e : null;
        public Element GetElement(Reference reference) => GetElement(reference.ElementId);
        public void Regenerate() { }
        internal Action CaptureRestore()
        {
            var originalElements = Elements.ToArray();
            var restores = new List<Action>();
            foreach (Element e in Elements.Values)
            {
                Location oldLocation = e.Location is LocationCurve lc ? new LocationCurve { Curve = lc.Curve } : e.Location;
                var parameters = e.Parameters.ToDictionary(p => p.Key, p => p.Value.Value);
                Transform placement = (e as FamilyInstance)?.Placement.Copy();
                ElementId typeId = e.TypeId, groupId = e.GroupId;
                bool pinned = e.Pinned;
                Element host = (e as FamilyInstance)?.Host;
                Reference face = (e as FamilyInstance)?.HostFace;
                FamilyInstance parent = (e as FamilyInstance)?.SuperComponent;
                var connectors = e is MEPCurve mc ? mc.ConnectorManager.Connectors : (e as FamilyInstance)?.MEPModel?.ConnectorManager.Connectors;
                var ports = (connectors ?? new ConnectorSet()).Select(c => new { C = c, c.Origin, Axes = c.CoordinateSystem.Copy(), Peers = c.AllRefs.ToArray() }).ToArray();
                restores.Add(() => { e.Location = oldLocation; e.TypeId = typeId; e.GroupId = groupId; e.Pinned = pinned; foreach (var p in parameters) e.Parameters[p.Key].Value = p.Value; if (placement != null) { var family = (FamilyInstance)e; family.Placement = placement; family.Host = host; family.HostFace = face; family.SuperComponent = parent; } foreach (var p in ports) { p.C.Origin = p.Origin; p.C.CoordinateSystem = p.Axes; p.C.AllRefs.Clear(); p.C.AllRefs.AddRange(p.Peers); } });
            }
            return () => { Elements.Clear(); foreach (var item in originalElements) Elements.Add(item.Key, item.Value); foreach (Action restore in restores) restore(); };
        }
    }
    public static class ElementTransformUtils { public static void MoveElement(Document doc, ElementId id, XYZ delta) { if (doc.MoveEffect == null) throw new InvalidOperationException("Fixture must specify movement observations"); doc.MoveEffect(id, delta); } }
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack, Pending }
    public enum FailureProcessingResult { Continue, ProceedWithRollBack }
    public interface IFailuresPreprocessor { FailureProcessingResult PreprocessFailures(FailuresAccessor accessor); }
    public enum FailureSeverity { Warning, Error, DocumentCorruption }
    public sealed class FailureMessageAccessor { public string Text; public FailureSeverity Severity = FailureSeverity.Warning; public string GetDescriptionText() => Text; public FailureSeverity GetSeverity() => Severity; }
    public sealed class FailuresAccessor { public IList<FailureMessageAccessor> Messages = new List<FailureMessageAccessor>(); public IList<FailureMessageAccessor> GetFailureMessages() => Messages; }
    public sealed class FailureHandlingOptions
    {
        internal IFailuresPreprocessor Processor;
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor p) { Processor = p; return this; }
        public FailureHandlingOptions SetClearAfterRollback(bool value) => this;
        public FailureHandlingOptions SetForcedModalHandling(bool value) => this;
    }
    public sealed class Transaction : IDisposable
    {
        private readonly Document doc; private TransactionStatus status; private Action restore; private FailureHandlingOptions options;
        public Transaction(Document doc, string name) { this.doc = doc; }
        public TransactionStatus Start() { restore = doc.CaptureRestore(); return status = TransactionStatus.Started; }
        public TransactionStatus GetStatus() => status;
        public FailureHandlingOptions GetFailureHandlingOptions() => new FailureHandlingOptions();
        public void SetFailureHandlingOptions(FailureHandlingOptions options) { this.options = options; }
        public TransactionStatus Commit()
        {
            doc.CommitEffect?.Invoke();
            if (options?.Processor?.PreprocessFailures(doc.PostedFailures) == FailureProcessingResult.ProceedWithRollBack || doc.CommitResult == TransactionStatus.RolledBack)
                return RollBack();
            return status = doc.CommitResult;
        }
        public TransactionStatus RollBack() { restore(); return status = TransactionStatus.RolledBack; }
        public void Dispose() { if (status == TransactionStatus.Started) RollBack(); }
    }
    public sealed class TransactionGroup : IDisposable
    {
        private readonly Document doc; private TransactionStatus status; private Action restore;
        public TransactionGroup(Document doc, string name) { this.doc = doc; }
        public TransactionStatus Start() { restore = doc.CaptureRestore(); return status = TransactionStatus.Started; }
        public TransactionStatus GetStatus() => status;
        public TransactionStatus Assimilate() => doc.AssimilateResult == TransactionStatus.RolledBack ? RollBack() : status = doc.AssimilateResult;
        public TransactionStatus RollBack() { restore(); doc.GroupRolledBack = true; return status = TransactionStatus.RolledBack; }
        public void Dispose() { if (status == TransactionStatus.Started) RollBack(); }
    }
}
namespace Autodesk.Revit.DB.Plumbing { public sealed class Pipe : Autodesk.Revit.DB.MEPCurve { } }
namespace Autodesk.Revit.UI
{
    [Flags] public enum TaskDialogCommonButtons { None = 0, Cancel = 8 }
    public enum TaskDialogCommandLinkId { CommandLink1 = 1001, CommandLink2, CommandLink3 }
    public enum TaskDialogResult { None, Cancel = 2, CommandLink1 = 1001, CommandLink2, CommandLink3 }
    public sealed class TaskDialog
    {
        public static readonly List<string> Messages = new List<string>();
        public static TaskDialogResult NextResult = TaskDialogResult.CommandLink2;
        public static TaskDialogResult ConnectionResult = TaskDialogResult.CommandLink1;
        private readonly string title;
        private readonly HashSet<TaskDialogResult> links = new HashSet<TaskDialogResult>(); private TaskDialogResult defaultButton;
        public TaskDialog(string title) { this.title = title; }
        public string MainInstruction { get; set; } public string MainContent { get; set; }
        public TaskDialogCommonButtons CommonButtons { get; set; }
        public TaskDialogResult DefaultButton { get => defaultButton; set { if (!links.Contains(value)) throw new ArgumentException("Corresponding button not found", "defaultButton"); defaultButton = value; } }
        public void AddCommandLink(TaskDialogCommandLinkId id, string text) => links.Add((TaskDialogResult)id);
        public TaskDialogResult Show() => title == "PipeLayer — Kết nối" ? ConnectionResult : NextResult;
        public static void Show(string title, string message) => Messages.Add(message);
    }
    public sealed class UIDocument { public Selection.Selection Selection { get; } = new Selection.Selection(); }
}
namespace Autodesk.Revit.UI.Selection
{
    public enum ObjectType { Element }
    public interface ISelectionFilter { }
    public sealed class Selection
    {
        public ICollection<Autodesk.Revit.DB.ElementId> Selected = new List<Autodesk.Revit.DB.ElementId>();
        public Autodesk.Revit.DB.Reference Pick;
        public ICollection<Autodesk.Revit.DB.ElementId> GetElementIds() => Selected;
        public Autodesk.Revit.DB.Reference PickObject(ObjectType type, ISelectionFilter filter, string prompt) => Pick;
    }
}
namespace VP_RevitAPI.Helper { public static class Isf { public sealed class Pipe : Autodesk.Revit.UI.Selection.ISelectionFilter { } } }
