using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets.DuplicateSheets;

internal sealed class CopyPasteWarningSwallower : IFailuresPreprocessor
{
	public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
	{
		IList<FailureMessageAccessor> messages = failuresAccessor.GetFailureMessages();
		foreach (FailureMessageAccessor message in messages)
		{
			if (message.GetSeverity() == FailureSeverity.Warning)
			{
				failuresAccessor.DeleteWarning(message);
			}
		}
		return FailureProcessingResult.Continue;
	}
}
