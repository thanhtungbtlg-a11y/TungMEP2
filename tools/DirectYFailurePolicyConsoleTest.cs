using System;
using VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYFailurePolicyConsoleTest
{
    private static int assertions;

    private static int Main()
    {
        AllowsOnlyMatchingTypeWarning();
        RejectsTypeErrorAndClassificationWarning();
        RejectsUnrelatedWarningsAndErrors();
        DoesNotHideFatalFailureBehindAllowedWarning();
        KeepsUsefulDiagnosticForMissingDescription();
        Console.WriteLine("DirectY failure policy: " + assertions + " assertions passed.");
        return 0;
    }

    private static void AllowsOnlyMatchingTypeWarning()
    {
        var policy = new DirectYFailurePolicy();
        Expect(policy.Record(true, true, "Different System Type"), "Only exact type warning may continue");
        Expect(policy.AllowedWarnings.Count == 1 && policy.AllowedWarnings[0] == "Different System Type",
            "Allowed type warning must remain visible to diagnostic logging");
        Expect(policy.LastFailure.Length == 0, "Allowed type warning must not become a fatal failure");
    }

    private static void RejectsTypeErrorAndClassificationWarning()
    {
        var typeError = new DirectYFailurePolicy();
        Expect(!typeError.Record(false, true, "System type error"), "Type mismatch must not hide error severity");
        Expect(typeError.LastFailure.Contains("System type error"), "Fatal type error needs its description");
        Expect(typeError.AllowedWarnings.Count == 0, "Fatal error is not an allowed warning");
        var classification = new DirectYFailurePolicy();
        Expect(!classification.Record(true, false, "Different System Classification; flow analysis unavailable"),
            "Classification warning must request rollback");
        Expect(classification.LastFailure.Contains("flow analysis unavailable"),
            "Classification diagnostic must explain the unsafe condition");
    }

    private static void RejectsUnrelatedWarningsAndErrors()
    {
        var policy = new DirectYFailurePolicy();
        Expect(!policy.Record(true, false, "Disconnected fitting"), "Unrelated warning must remain fatal");
        Expect(!policy.Record(false, false, "Invalid pipe geometry"), "Unrelated error must remain fatal");
        Expect(policy.LastFailure.Contains("Disconnected fitting") && policy.LastFailure.Contains("Invalid pipe geometry"),
            "Both blocked messages must remain available after preprocessing");
    }

    private static void DoesNotHideFatalFailureBehindAllowedWarning()
    {
        var policy = new DirectYFailurePolicy();
        Expect(!policy.Record(true, false, "Classification mismatch"), "Classification warning must be blocked");
        Expect(policy.Record(true, true, "Different System Type"), "Compatible type warning is independently allowed");
        Expect(policy.LastFailure.Contains("Classification mismatch"), "Later allowed warning must not erase fatal reason");
        Expect(policy.AllowedWarnings.Count == 1, "Allowed warning must still be recorded in a mixed failure batch");
    }

    private static void KeepsUsefulDiagnosticForMissingDescription()
    {
        var policy = new DirectYFailurePolicy();
        Expect(!policy.Record(true, false, " "), "Unidentified warning must remain fatal");
        Expect(!string.IsNullOrWhiteSpace(policy.LastFailure), "Missing description must receive a useful fallback");
    }

    private static void Expect(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + name);
    }
}
