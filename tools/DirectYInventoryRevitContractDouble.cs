using System;
using System.Collections.Generic;
using System.Linq;

// Test-only adapter for the Revit inventory boundary. Revit's real Document cannot be created
// outside Revit. This double follows the verified Revit2024 XML collector contract, including
// the mandatory native filter; it is NOT a native/runtime integration test.
namespace Autodesk.Revit.DB
{
    internal sealed class ElementId
    {
        internal ElementId(long value) { Value = value; }
        internal long Value { get; }
    }
    internal sealed class InventoryElement
    {
        internal InventoryElement(long id, bool type = false, bool system = false)
        { Id = new ElementId(id); IsType = type; IsSystem = system; }
        internal ElementId Id { get; }
        internal bool IsType { get; }
        internal bool IsSystem { get; }
    }
    internal sealed class Document
    {
        internal readonly List<InventoryElement> Elements = new List<InventoryElement>();
    }
    internal class MEPSystem { }

    internal sealed class FilteredElementCollector : IDisposable
    {
        private readonly Document document;
        private Func<InventoryElement, bool> predicate;
        internal FilteredElementCollector(Document document) { this.document = document; }
        internal FilteredElementCollector WhereElementIsNotElementType() => Apply(element => !element.IsType);
        internal FilteredElementCollector WhereElementIsElementType() => Apply(element => element.IsType);
        internal FilteredElementCollector OfClass(Type type)
        {
            if (type != typeof(MEPSystem)) throw new NotSupportedException("Only the inventory boundary is implemented.");
            return Apply(element => element.IsSystem);
        }
        private FilteredElementCollector Apply(Func<InventoryElement, bool> filter)
        {
            var previous = predicate;
            predicate = previous == null ? filter : element => previous(element) && filter(element);
            return this;
        }
        internal ICollection<ElementId> ToElementIds()
        {
            if (predicate == null) throw new InvalidOperationException(
                "The collector does not have a filter applied. Extraction or iteration of elements is not permitted without a filter.");
            return document.Elements.Where(predicate).Select(element => element.Id).ToArray();
        }
        public void Dispose() { }
    }
}
