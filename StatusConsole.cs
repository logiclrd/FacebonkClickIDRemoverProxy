using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace FacebonkClickIDRemoverProxy
{
	class StatusConsole
	{
		class OngoingRequest
		{
			public int RequestID;
			public string ClientIP;
			public string Method;
			public string Path;
			public string MethodPathString;
			public string State;
			public long BytesSoFar;
			public long BytesExpected;
		}

		List<OngoingRequest> _requests = new List<OngoingRequest>();
		Dictionary<int, OngoingRequest> _requestByID = new Dictionary<int, OngoingRequest>();

		static object s_sync = new object();

		static void Deliver(TextWriter stream, string message)
		{
			lock (s_sync)
			{
				stream.WriteLine(message);
				stream.Flush();
			}
		}

		static void Deliver(TextWriter stream, string format, params object[] args)
		{
			Deliver(stream, string.Format(format, args));
		}

		public static void NotifyNewRequest(TextWriter stream, int requestID, string clientIP, string method, string path)
		{
			Deliver(stream, "{0} new {1} {2} {3}", requestID, clientIP, method, path);
		}

		public static void NotifyRequestSent(TextWriter stream, int requestID)
		{
			Deliver(stream, "{0} sent", requestID);
		}

		public static void NotifyRequestLength(TextWriter stream, int requestID, long expectedLength)
		{
			Deliver(stream, "{0} length {1}", requestID, expectedLength);
		}

		public static void NotifyRequestProgress(TextWriter stream, int requestID, long bytesProgress)
		{
			Deliver(stream, "{0} progress {1}", requestID, bytesProgress);
		}

		public static void NotifyRequestEnd(TextWriter stream, int requestID)
		{
			Deliver(stream, "{0} end", requestID);
		}

		public const string StatusConsoleArguments = "/statusconsole";

		public static IStatusConsoleSender Launch()
		{
			var processStartInfo = new ProcessStartInfo();

			processStartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
			processStartInfo.Arguments = StatusConsoleArguments;
			processStartInfo.RedirectStandardInput = true;

			var process = ProcessEx.StartNewConsole(processStartInfo);

			return new StatusConsoleSender(process.StandardInput);
		}

		public StatusConsole()
		{
			_updateSource = new ActionBlock<string>(ProcessUpdate);
		}

		ActionBlock<string> _updateSource;

		public bool Run()
		{
			Console.CursorVisible = false;

			var args = Environment.GetCommandLineArgs();

			if ((args.Length < 2) || (args[1] != StatusConsoleArguments))
				return false;

			Console.SetWindowSize(150, 40);
			Console.SetBufferSize(150, 40);

			_exiting = false;

			new Thread(RenderRequestsThread).Start();

			while (true)
			{
				string update = Console.ReadLine();

				if (update == null)
					break;

				_updateSource.Post(update);
			}

			_updateSource.Complete();

			_exiting = true;
			_renderTrigger.Set();

			_updateSource.Completion.Wait();

			return true;
		}

		void ProcessUpdate(string update)
		{
			try
			{
				string[] parts = update.Split(' ');

				int requestID = int.Parse(parts[0]);
				string updateType = parts[1];

				if (!_requestByID.TryGetValue(requestID, out var request)
					&& (updateType != "new"))
					return;

				switch (updateType)
				{
					case "new":
					{
						if (request != null)
							_requests.Remove(request);

						request = new OngoingRequest();
						request.RequestID = requestID;
						request.ClientIP = parts[2];
						request.Method = parts[3];
						request.Path = parts[4];
						request.State = "Connect";

						if (request.ClientIP.Length > 15)
							request.ClientIP = request.ClientIP.Substring(0, 15);

						_requests.Add(request);
						_requestByID[requestID] = request;

						break;
					}
					case "sent":
					{
						request.State = "Sent";
						break;
					}
					case "length":
					{
						request.State = "Stream";
						request.BytesExpected = long.Parse(parts[2]);
						break;
					}
					case "progress":
					{
						request.State = "Stream";
						request.BytesSoFar += long.Parse(parts[2]);
						break;
					}
					case "end":
					{
						request.State = "Done";

						var delayTask = Task.Delay(200);

						delayTask.ConfigureAwait(false);
						delayTask.ContinueWith(
							(task) =>
							{
								_updateSource.Post(requestID + " remove");
							});

						break;
					}
					case "remove":
					{
						_requests.Remove(request);
						_requestByID.Remove(requestID);
						break;
					}
				}

				_renderTrigger.Set();
			}
			catch (Exception e)
			{
				Console.WriteLine("EXCEPTION: " + e);
				_exiting = true;
			}
		}

		AutoResetEvent _renderTrigger = new AutoResetEvent(initialState: true);
		bool _exiting = false;

		void RenderRequestsThread()
		{
			while (true)
			{
				_renderTrigger.WaitOne();

				if (_exiting)
					break;

				RenderRequests();
			}
		}

		void RenderRequests()
		{
			Console.SetCursorPosition(0, 0);

			for (int i = 0; i < Console.WindowHeight - 1; i++)
			{
				if (i < _requests.Count)
					RenderRequest(_requests[i]);
				else
					Console.Write(GetSpaceString(Console.WindowWidth - 1));

				Console.WriteLine();
			}

			if (_requests.Count > Console.WindowHeight)
				Console.Write("...");
			else if (_requests.Count == Console.WindowHeight)
				RenderRequest(_requests[_requests.Count - 1]);
		}

		void RenderRequest(OngoingRequest request)
		{
			int width = Console.WindowWidth - 1;

			Console.Write("{0,9} | ", request.RequestID);
			width -= 12;

			Console.Write("{0,-15} | ", request.ClientIP);
			width -= 18;

			if (request.MethodPathString == null)
			{
				const int MethodPathStringWidth = 23;

				request.MethodPathString = request.Method + " " + request.Path;

				if (request.MethodPathString.Length > MethodPathStringWidth)
				{
					int queryStringStart = request.MethodPathString.IndexOf('?');

					int totalQueryStringCharacters = request.MethodPathString.Length - queryStringStart;
					int removeCharacters = request.MethodPathString.Length - MethodPathStringWidth;
					int visibleCharacters = totalQueryStringCharacters - removeCharacters - 3; // factor in "..."
					int visibleCharactersBeforeEllipsis = visibleCharacters / 2;
					int visibleCharactersAfterEllipsis = visibleCharacters - visibleCharactersBeforeEllipsis;

					// abcde?123456789
					// total: 9
					// remove: 2
					// abcde?12...89

					if ((visibleCharactersBeforeEllipsis > 1) && (visibleCharactersAfterEllipsis > 1))
					{
						request.MethodPathString =
							request.MethodPathString.Substring(0, queryStringStart + visibleCharactersBeforeEllipsis + 1) +
							"..." +
							request.MethodPathString.Substring(request.MethodPathString.Length - visibleCharactersAfterEllipsis);
					}
					else
						request.MethodPathString = request.MethodPathString.Substring(0, queryStringStart + 1) + "...";

					if (request.MethodPathString.Length > MethodPathStringWidth)
					{
						removeCharacters = request.MethodPathString.Length - MethodPathStringWidth;
						visibleCharacters = queryStringStart - removeCharacters - 3; // factor in "..."
						visibleCharactersBeforeEllipsis = visibleCharacters / 2;
						visibleCharactersAfterEllipsis = visibleCharacters - visibleCharactersBeforeEllipsis;

						if ((visibleCharactersBeforeEllipsis > 1) && (visibleCharactersAfterEllipsis > 1))
						{
							request.MethodPathString =
								request.MethodPathString.Substring(0, visibleCharactersBeforeEllipsis) +
								"..." +
								request.MethodPathString.Substring(visibleCharactersBeforeEllipsis + removeCharacters);
						}

						if (request.MethodPathString.Length > MethodPathStringWidth)
							request.MethodPathString = request.MethodPathString.Substring(0, MethodPathStringWidth - 3) + "...";
					}
				}

				if (request.MethodPathString.Length < MethodPathStringWidth)
					request.MethodPathString += GetSpaceString(MethodPathStringWidth - request.MethodPathString.Length);

				request.MethodPathString += " | ";
			}

			Console.Write(request.MethodPathString);
			width -= request.MethodPathString.Length;

			Console.Write("{0,7} | ", request.State);
			width -= 10;

			if (request.BytesExpected > 0)
			{
				string fraction = string.Format("{0:#,###,###,##0} / {1:#,###,###,##0} ", request.BytesSoFar, request.BytesExpected);

				Console.Write(fraction);

				width -= fraction.Length;

				int progressBarWidth = width - 2;
				int progressChars = (int)((request.BytesSoFar * progressBarWidth + request.BytesExpected / 2) / request.BytesExpected);

				Console.Write(GetProgressFilledString(progressChars));
				Console.Write(GetProgressUnfilledString(progressBarWidth - progressChars));
			}
			else
			{
				string progress = string.Format("{0:#,###,###,##0}", request.BytesSoFar);

				Console.Write(progress);

				width -= progress.Length;

				Console.Write(GetSpaceString(width));
			}
		}

		Dictionary<int, string> _progressFilledStrings = new Dictionary<int, string>();
		Dictionary<int, string> _progressUnfilledStrings = new Dictionary<int, string>();
		Dictionary<int, string> _spaceStrings = new Dictionary<int, string>();

		string GetProgressFilledString(int numChars)
		{
			if (!_progressFilledStrings.TryGetValue(numChars, out var value))
				value = _progressFilledStrings[numChars] = "[" + new string('#', numChars);

			return value;
		}

		string GetProgressUnfilledString(int numChars)
		{
			if (!_progressUnfilledStrings.TryGetValue(numChars, out var value))
				value = _progressUnfilledStrings[numChars] = new string('.', numChars) + "]";

			return value;
		}

		string GetSpaceString(int numChars)
		{
			if (!_spaceStrings.TryGetValue(numChars, out var value))
				value = _spaceStrings[numChars] = new string(' ', numChars);

			return value;
		}
	}
}
