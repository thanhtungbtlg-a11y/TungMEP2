namespace VP_RevitAPI.Internal.Common;

internal enum MepAlignmentConnectionStrategy
{
	Endpoint,
	PipeBody,
	AlignOnly
}

internal static class MepPipeBodyConnectionPolicy
{
	internal static MepAlignmentConnectionStrategy ChooseStrategy(
		MepConnectorAlignmentMode mode,
		bool hasOpenOpenPair,
		bool fixedIsPipe,
		bool fixedHasOpenEnd,
		bool movingSelectedIsPipe,
		bool movingSelectedHasOpenEnd)
	{
		if (mode == MepConnectorAlignmentMode.AlignIn3D)
		{
			return MepAlignmentConnectionStrategy.AlignOnly;
		}

		if (hasOpenOpenPair)
		{
			return MepAlignmentConnectionStrategy.Endpoint;
		}

		if (mode == MepConnectorAlignmentMode.MoveConnectAlign &&
		    fixedIsPipe &&
		    !fixedHasOpenEnd &&
		    movingSelectedIsPipe &&
		    movingSelectedHasOpenEnd)
		{
			return MepAlignmentConnectionStrategy.PipeBody;
		}

		return MepAlignmentConnectionStrategy.AlignOnly;
	}
}
