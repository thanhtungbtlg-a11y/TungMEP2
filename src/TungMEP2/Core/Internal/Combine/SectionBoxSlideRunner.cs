using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Combine;

internal static class SectionBoxSlideRunner
{
	private static UI_SectionBoxSlideWindow? _window;

	private static UI_ExternalEventBridge? _externalEventBridge;

	internal static void Run()
	{
		UI_HandyControlBootstrapper.EnsureInitialized();
		if (_externalEventBridge == null)
		{
			_externalEventBridge = new UI_ExternalEventBridge();
		}
		if (_window == null)
		{
			_window = new UI_SectionBoxSlideWindow(_externalEventBridge);
			_window.Closed += delegate
			{
				_window = null;
			};
			_window.Show();
		}
		else
		{
			if (!_window.IsVisible)
			{
				_window.Show();
			}
			_window.Activate();
		}
	}
}
