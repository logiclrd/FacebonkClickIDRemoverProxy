using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace FacebonkClickIDRemoverProxy
{
	class ProcessEx
	{
		static object s_CreateProcessLock = new object();

		public static Process StartNewConsole(ProcessStartInfo startInfo)
		{
			if (startInfo.StandardOutputEncoding != null && !startInfo.RedirectStandardOutput)
				throw new InvalidOperationException("StandardOutputEncoding not allowed when not redirecting standard output");
			if (startInfo.StandardErrorEncoding != null && !startInfo.RedirectStandardError)
				throw new InvalidOperationException("StandardErrorEncoding not allowed when not redirecting standard output");

			// See knowledge base article Q190351 for an explanation of the following code.  Noteworthy tricky points:
			//    * The handles are duplicated as non-inheritable before they are passed to CreateProcess so
			//      that the child process can not close them
			//    * CreateProcess allows you to redirect all or none of the standard IO handles, so we use
			//      GetStdHandle for the handles that are not being redirected

			StringBuilder commandLine = BuildCommandLine(startInfo.FileName, startInfo.Arguments);

			Func<SafeProcessHandle> new_SafeProcessHandle =
				() => (SafeProcessHandle)typeof(SafeProcessHandle).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null).Invoke(null);
			Action<SafeProcessHandle, IntPtr> SafeProcessHandle_InitialSetHandle =
				(safeHandle, unmanagedHandle) => typeof(SafeProcessHandle).GetMethod("InitialSetHandle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(safeHandle, new object[] { unmanagedHandle });

			Func<ProcessStartInfo, IDictionary<string, string>> ProcessStartInfo_Get_environmentVariables =
				(psi) => (IDictionary<string, string>)typeof(ProcessStartInfo).GetField("_environmentVariables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(psi);

			NativeMethods.STARTUPINFO startupInfo = new NativeMethods.STARTUPINFO();
			SafeNativeMethods.PROCESS_INFORMATION processInfo = new SafeNativeMethods.PROCESS_INFORMATION();
			SafeProcessHandle procSH = new_SafeProcessHandle();
			SafeThreadHandle threadSH = new SafeThreadHandle();
			bool retVal;
			int errorCode = 0;
			// handles used in parent process
			SafeFileHandle standardInputWritePipeHandle = null;
			SafeFileHandle standardOutputReadPipeHandle = null;
			SafeFileHandle standardErrorReadPipeHandle = null;
			GCHandle environmentHandle = new GCHandle();
			lock (s_CreateProcessLock)
			{
				try
				{
					// set up the streams
					if (startInfo.RedirectStandardInput || startInfo.RedirectStandardOutput || startInfo.RedirectStandardError)
					{
						if (startInfo.RedirectStandardInput)
						{
							CreatePipe(out standardInputWritePipeHandle, out startupInfo.hStdInput, true);
						}
						else
						{
							startupInfo.hStdInput = new SafeFileHandle(NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE), false);
						}

						if (startInfo.RedirectStandardOutput)
						{
							CreatePipe(out standardOutputReadPipeHandle, out startupInfo.hStdOutput, false);
						}
						else
						{
							startupInfo.hStdOutput = new SafeFileHandle(NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE), false);
						}

						if (startInfo.RedirectStandardError)
						{
							CreatePipe(out standardErrorReadPipeHandle, out startupInfo.hStdError, false);
						}
						else
						{
							startupInfo.hStdError = new SafeFileHandle(NativeMethods.GetStdHandle(NativeMethods.STD_ERROR_HANDLE), false);
						}

						startupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;
					}

					// set up the creation flags paramater
					int creationFlags = NativeMethods.CREATE_NEW_CONSOLE;
					if (startInfo.CreateNoWindow) creationFlags |= NativeMethods.CREATE_NO_WINDOW;

					// set up the environment block parameter
					IntPtr environmentPtr = (IntPtr)0;

					if (ProcessStartInfo_Get_environmentVariables(startInfo) != null)
					{
						bool unicode = true;
						creationFlags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;

						byte[] environmentBytes = EnvironmentBlock.ToByteArray(ProcessStartInfo_Get_environmentVariables(startInfo), unicode);
						environmentHandle = GCHandle.Alloc(environmentBytes, GCHandleType.Pinned);
						environmentPtr = environmentHandle.AddrOfPinnedObject();
					}

					string workingDirectory = startInfo.WorkingDirectory;
					if (workingDirectory == string.Empty)
						workingDirectory = Environment.CurrentDirectory;

					if (startInfo.UserName.Length != 0)
						throw new Exception("Logon feature not supported by this implementation");

					RuntimeHelpers.PrepareConstrainedRegions();
					try { }
					finally
					{
						retVal = NativeMethods.CreateProcess(
										null,               // we don't need this since all the info is in commandLine
										commandLine,        // pointer to the command line string
										null,               // pointer to process security attributes, we don't need to inheriat the handle
										null,               // pointer to thread security attributes
										true,               // handle inheritance flag
										creationFlags,      // creation flags
										environmentPtr,     // pointer to new environment block
										workingDirectory,   // pointer to current directory name
										startupInfo,        // pointer to STARTUPINFO
										processInfo         // pointer to PROCESS_INFORMATION
								);
						if (!retVal)
							errorCode = Marshal.GetLastWin32Error();
						if (processInfo.hProcess != (IntPtr)0 && processInfo.hProcess != (IntPtr)NativeMethods.INVALID_HANDLE_VALUE)
							SafeProcessHandle_InitialSetHandle(procSH, processInfo.hProcess);
						if (processInfo.hThread != (IntPtr)0 && processInfo.hThread != (IntPtr)NativeMethods.INVALID_HANDLE_VALUE)
							threadSH.InitialSetHandle(processInfo.hThread);
					}

					if (!retVal)
					{
						if (errorCode == NativeMethods.ERROR_BAD_EXE_FORMAT || errorCode == NativeMethods.ERROR_EXE_MACHINE_TYPE_MISMATCH)
						{
							throw new Win32Exception(errorCode, "Invalid application");
						}
						throw new Win32Exception(errorCode);
					}
				}
				finally
				{
					// free environment block
					if (environmentHandle.IsAllocated)
					{
						environmentHandle.Free();
					}

					startupInfo.Dispose();
				}
			}

			var process = new Process();

			Action<Process, StreamWriter> Process_Set_standardInput =
				(process, standardInput) => typeof(Process).GetField("_standardInput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(process, standardInput);
			Action<Process, StreamReader> Process_Set_standardOutput =
				(process, standardOutput) => typeof(Process).GetField("_standardOutput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(process, standardOutput);
			Action<Process, StreamReader> Process_Set_standardError =
				(process, standardError) => typeof(Process).GetField("_standardError", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(process, standardError);
			Action<Process, SafeProcessHandle> Process_SetProcessHandle =
				(process, procSH) => typeof(Process).GetMethod("SetProcessHandle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(process, new[] { procSH });
			Action<Process, int> Process_SetProcessId =
				(process, procId) => typeof(Process).GetMethod("SetProcessId", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(process, new object[] { procId });

			if (startInfo.RedirectStandardInput)
			{
				var standardInput = new StreamWriter(new FileStream(standardInputWritePipeHandle, FileAccess.Write, 4096, false), Console.InputEncoding, 4096);
				standardInput.AutoFlush = true;

				Process_Set_standardInput(process, standardInput);
			}
			if (startInfo.RedirectStandardOutput)
			{
				Encoding enc = (startInfo.StandardOutputEncoding != null) ? startInfo.StandardOutputEncoding : Console.OutputEncoding;
				var standardOutput = new StreamReader(new FileStream(standardOutputReadPipeHandle, FileAccess.Read, 4096, false), enc, true, 4096);

				Process_Set_standardOutput(process, standardOutput);
			}
			if (startInfo.RedirectStandardError)
			{
				Encoding enc = (startInfo.StandardErrorEncoding != null) ? startInfo.StandardErrorEncoding : Console.OutputEncoding;
				var standardError = new StreamReader(new FileStream(standardErrorReadPipeHandle, FileAccess.Read, 4096, false), enc, true, 4096);

				Process_Set_standardError(process, standardError);
			}

			if (procSH.IsInvalid)
				throw new Exception("Failed to create process");

			Process_SetProcessHandle(process, procSH);
			Process_SetProcessId(process, processInfo.dwProcessId);

			threadSH.Close();

			return process;
		}

		private static StringBuilder BuildCommandLine(string executableFileName, string arguments)
		{
			// Construct a StringBuilder with the appropriate command line
			// to pass to CreateProcess.  If the filename isn't already 
			// in quotes, we quote it here.  This prevents some security
			// problems (it specifies exactly which part of the string
			// is the file to execute).
			StringBuilder commandLine = new StringBuilder();
			string fileName = executableFileName.Trim();
			bool fileNameIsQuoted = (fileName.StartsWith("\"", StringComparison.Ordinal) && fileName.EndsWith("\"", StringComparison.Ordinal));
			if (!fileNameIsQuoted)
			{
				commandLine.Append("\"");
			}

			commandLine.Append(fileName);

			if (!fileNameIsQuoted)
			{
				commandLine.Append("\"");
			}

			if (!String.IsNullOrEmpty(arguments))
			{
				commandLine.Append(" ");
				commandLine.Append(arguments);
			}

			return commandLine;
		}

		[ResourceExposure(ResourceScope.None)]
		[ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)]
		private static void CreatePipe(out SafeFileHandle parentHandle, out SafeFileHandle childHandle, bool parentInputs)
		{
			NativeMethods.SECURITY_ATTRIBUTES securityAttributesParent = new NativeMethods.SECURITY_ATTRIBUTES();
			securityAttributesParent.bInheritHandle = true;

			SafeFileHandle hTmp = null;
			try
			{
				if (parentInputs)
				{
					CreatePipeWithSecurityAttributes(out childHandle, out hTmp, securityAttributesParent, 0);
				}
				else
				{
					CreatePipeWithSecurityAttributes(out hTmp,
																								out childHandle,
																								securityAttributesParent,
																								0);
				}
				// Duplicate the parent handle to be non-inheritable so that the child process 
				// doesn't have access. This is done for correctness sake, exact reason is unclear.
				// One potential theory is that child process can do something brain dead like 
				// closing the parent end of the pipe and there by getting into a blocking situation
				// as parent will not be draining the pipe at the other end anymore. 
				if (!NativeMethods.DuplicateHandle(new HandleRef(null, NativeMethods.GetCurrentProcess()),
																													 hTmp,
																													 new HandleRef(null, NativeMethods.GetCurrentProcess()),
																													 out parentHandle,
																													 0,
																													 false,
																													 NativeMethods.DUPLICATE_SAME_ACCESS))
				{
					throw new Win32Exception();
				}
			}
			finally
			{
				if (hTmp != null && !hTmp.IsInvalid)
				{
					hTmp.Close();
				}
			}
		}

		[ResourceExposure(ResourceScope.Process)]
		[ResourceConsumption(ResourceScope.Process)]
		private static void CreatePipeWithSecurityAttributes(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, NativeMethods.SECURITY_ATTRIBUTES lpPipeAttributes, int nSize)
		{
			bool ret = NativeMethods.CreatePipe(out hReadPipe, out hWritePipe, lpPipeAttributes, nSize);
			if (!ret || hReadPipe.IsInvalid || hWritePipe.IsInvalid)
			{
				throw new Win32Exception();
			}
		}

		internal static class EnvironmentBlock
		{
			public static byte[] ToByteArray(IDictionary<string, string> sd, bool unicode)
			{
				// create a list of null terminated "key=val" strings
				StringBuilder stringBuff = new StringBuilder();
				foreach (var entry in sd.OrderBy(e => e.Key))
				{
					stringBuff.Append(entry.Key);
					stringBuff.Append('=');
					stringBuff.Append(entry.Value);
					stringBuff.Append('\0');
				}
				// an extra null at the end indicates end of list.
				stringBuff.Append('\0');

				byte[] envBlock;

				if (unicode)
				{
					envBlock = Encoding.Unicode.GetBytes(stringBuff.ToString());
				}
				else
				{
					envBlock = Encoding.Default.GetBytes(stringBuff.ToString());

					if (envBlock.Length > UInt16.MaxValue)
						throw new InvalidOperationException("Environment block too long");
				}

				return envBlock;
			}
		}
	}
}
