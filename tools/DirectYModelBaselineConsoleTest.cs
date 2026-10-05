using System;
using Autodesk.Revit.DB;
using VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYModelBaselineConsoleTest
{
    private static int assertions;
    private static int Main()
    {
        try
        {
            CapturesInstancesAndTypesWithoutCollectorError();
            RejectsLostExistingInstance();
            RejectsLostExistingType();
            AllowsNativeManagedSystemMerge();
            NewElementsDoNotBecomeBaseline();
            Console.WriteLine("DirectY model inventory contract tests passed: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception exception)
        { Console.Error.WriteLine("FAIL: " + exception.Message); return 1; }
    }
    private static Document Project()
    {
        var document = new Document();
        document.Elements.AddRange(new[] { new InventoryElement(1), new InventoryElement(2),
            new InventoryElement(50, type:true), new InventoryElement(100, system:true) });
        return document;
    }
    private static void CapturesInstancesAndTypesWithoutCollectorError()
    {
        var document = Project();
        var baseline = new DirectYModelBaseline(document);
        Expect(baseline.AllIds.SetEquals(new long[] { 1, 2, 50, 100 }), "Capture must protect both model instances and types.");
        Expect(baseline.ElementsSurvive(document, out var error), "An unchanged model must pass inventory checks without an unfiltered-collector error.");
        Expect(error.Length == 0, "Unchanged model must not report loss.");
    }
    private static void RejectsLostExistingInstance()
    {
        var document = Project(); var baseline = new DirectYModelBaseline(document);
        document.Elements.RemoveAll(element => element.Id.Value == 2);
        Expect(!baseline.ElementsSurvive(document, out var error), "A lost existing instance must still be rejected.");
        Expect(error.Contains("2"), "Lost instance diagnostic must identify its ID.");
    }
    private static void RejectsLostExistingType()
    {
        var document = Project(); var baseline = new DirectYModelBaseline(document);
        document.Elements.RemoveAll(element => element.Id.Value == 50);
        Expect(!baseline.ElementsSurvive(document, out var error), "Filtering must not weaken protection of existing types.");
        Expect(error.Contains("50"), "Lost type diagnostic must identify its ID.");
    }
    private static void AllowsNativeManagedSystemMerge()
    {
        var document = Project(); var baseline = new DirectYModelBaseline(document);
        document.Elements.RemoveAll(element => element.Id.Value == 100);
        Expect(baseline.ElementsSurvive(document, out var error), "Native merge of managed system records remains supported.");
        Expect(error.Length == 0, "Native managed-system merge must not report physical loss.");
    }
    private static void NewElementsDoNotBecomeBaseline()
    {
        var document = Project(); var baseline = new DirectYModelBaseline(document);
        document.Elements.Add(new InventoryElement(900));
        Expect(baseline.ElementsSurvive(document, out _), "Added objects must not make existing inventory fail.");
        Expect(baseline.AllIds.SetEquals(new long[] { 1, 2, 50, 100 }), "Baseline must remain immutable through later checks.");
    }
    private static void Expect(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
}
