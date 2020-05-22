using System;
using System.IO;

namespace FacebonkClickIDRemoverProxy
{
	class StatusConsoleSender : IStatusConsoleSender
	{
		TextWriter _statusConsoleInputStream;

		public StatusConsoleSender(TextWriter statusConsoleInputStream)
		{
			_statusConsoleInputStream = statusConsoleInputStream;
		}

		void AutoDetach(Action action)
		{
			try
			{
				if (_statusConsoleInputStream != null)
					action();
			}
			catch
			{
				_statusConsoleInputStream = null;
			}
		}

		public void NotifyNewRequest(int requestID, string clientIP, string method, string path) => AutoDetach(() => StatusConsole.NotifyNewRequest(_statusConsoleInputStream, requestID, clientIP, method, path));
		public void NotifyRequestSent(int requestID) => AutoDetach(() => StatusConsole.NotifyRequestSent(_statusConsoleInputStream, requestID));
		public void NotifyRequestLength(int requestID, long expectedLength) => AutoDetach(() => StatusConsole.NotifyRequestLength(_statusConsoleInputStream, requestID, expectedLength));
		public void NotifyRequestProgress(int requestID, long bytesProgress) => AutoDetach(() => StatusConsole.NotifyRequestProgress(_statusConsoleInputStream, requestID, bytesProgress));
		public void NotifyRequestEnd(int requestID) => AutoDetach(() => StatusConsole.NotifyRequestEnd(_statusConsoleInputStream, requestID));
	}
}
