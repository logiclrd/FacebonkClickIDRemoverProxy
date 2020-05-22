using System;
using System.IO;

namespace FacebonkClickIDRemoverProxy
{
	public interface IStatusConsoleSender
	{
		void NotifyNewRequest(int requestID, string clientIP, string method, string path);
		void NotifyRequestSent(int requestID);
		void NotifyRequestLength(int requestID, long expectedLength);
		void NotifyRequestProgress(int requestID, long bytesProgress);
		void NotifyRequestEnd(int requestID);
	}
}
