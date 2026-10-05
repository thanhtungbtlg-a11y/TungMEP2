using System;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.UI;

public sealed class UI_ExternalEventBridge : IDisposable
{
	private readonly object _sync = new object();

	private readonly ExternalEvent _externalEvent;

	private UI_Request? _pendingRequest;

	public UI_ExternalEventBridge()
	{
		_externalEvent = ExternalEvent.Create(new UI_ExternalEventHandler(this));
	}

	public void Raise(UI_Request request)
	{
		if (request == null)
		{
			throw new ArgumentNullException("request");
		}
		lock (_sync)
		{
			_pendingRequest = request;
		}
		_externalEvent.Raise();
	}

	public bool TryDequeue(out UI_Request request)
	{
		lock (_sync)
		{
			if (_pendingRequest == null)
			{
				request = null;
				return false;
			}
			request = _pendingRequest;
			_pendingRequest = null;
			return true;
		}
	}

	public void Dispose()
	{
		_externalEvent.Dispose();
	}
}
