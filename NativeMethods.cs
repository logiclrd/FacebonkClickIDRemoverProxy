using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace FacebonkClickIDRemoverProxy
{
	class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential)]
		internal class STARTUPINFO
		{
			public int cb;
			public IntPtr lpReserved = IntPtr.Zero;
			public IntPtr lpDesktop = IntPtr.Zero;
			public IntPtr lpTitle = IntPtr.Zero;
			public int dwX = 0;
			public int dwY = 0;
			public int dwXSize = 0;
			public int dwYSize = 0;
			public int dwXCountChars = 0;
			public int dwYCountChars = 0;
			public int dwFillAttribute = 0;
			public int dwFlags = 0;
			public short wShowWindow = 0;
			public short cbReserved2 = 0;
			public IntPtr lpReserved2 = IntPtr.Zero;
			public SafeFileHandle hStdInput = new SafeFileHandle(IntPtr.Zero, false);
			public SafeFileHandle hStdOutput = new SafeFileHandle(IntPtr.Zero, false);
			public SafeFileHandle hStdError = new SafeFileHandle(IntPtr.Zero, false);

			public STARTUPINFO()
			{
				cb = Marshal.SizeOf(this);
			}

			public void Dispose()
			{
				// close the handles created for child process
				if (hStdInput != null && !hStdInput.IsInvalid)
				{
					hStdInput.Close();
					hStdInput = null;
				}

				if (hStdOutput != null && !hStdOutput.IsInvalid)
				{
					hStdOutput.Close();
					hStdOutput = null;
				}

				if (hStdError != null && !hStdError.IsInvalid)
				{
					hStdError.Close();
					hStdError = null;
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		internal class SECURITY_ATTRIBUTES
		{
			public int nLength = 12;
			public IntPtr lpSecurityDescriptor = IntPtr.Zero;
			public bool bInheritHandle = false;
		}

		public const int CREATE_NO_WINDOW = 0x08000000;
		public const int CREATE_NEW_CONSOLE = 0x00000010;
		public const int CREATE_SUSPENDED = 0x00000004;
		public const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;

		public const int DUPLICATE_SAME_ACCESS = 2;

		public const int ERROR_BAD_EXE_FORMAT = 193;
		public const int ERROR_EXE_MACHINE_TYPE_MISMATCH = 216;

		public const int STARTF_USESTDHANDLES = 0x00000100;

		public const int STD_INPUT_HANDLE = -10;
		public const int STD_OUTPUT_HANDLE = -11;
		public const int STD_ERROR_HANDLE = -12;

		public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

		[DllImport("Kernel32", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
		[ResourceExposure(ResourceScope.Process)]
		public static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

		[DllImport("Kernel32", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true, BestFitMapping = false)]
		[ResourceExposure(ResourceScope.Process)]
		public static extern bool CreateProcess(
					[MarshalAs(UnmanagedType.LPTStr)]
						string lpApplicationName,                   // LPCTSTR
					StringBuilder lpCommandLine,                // LPTSTR - note: CreateProcess might insert a null somewhere in this string
					SECURITY_ATTRIBUTES lpProcessAttributes,    // LPSECURITY_ATTRIBUTES
					SECURITY_ATTRIBUTES lpThreadAttributes,     // LPSECURITY_ATTRIBUTES
					bool bInheritHandles,                        // BOOL
					int dwCreationFlags,                        // DWORD
					IntPtr lpEnvironment,                       // LPVOID
					[MarshalAs(UnmanagedType.LPTStr)]
						string lpCurrentDirectory,                  // LPCTSTR
					STARTUPINFO lpStartupInfo,                  // LPSTARTUPINFO
					SafeNativeMethods.PROCESS_INFORMATION lpProcessInformation    // LPPROCESS_INFORMATION
			);

		[DllImport("Kernel32", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
		[ResourceExposure(ResourceScope.Machine)]
		public static extern bool DuplicateHandle(
				 HandleRef hSourceProcessHandle,
				 SafeHandle hSourceHandle,
				 HandleRef hTargetProcess,
				 out SafeFileHandle targetHandle,
				 int dwDesiredAccess,
				 bool bInheritHandle,
				 int dwOptions
		 );

		[DllImport("Kernel32", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
		[ResourceExposure(ResourceScope.Process)]
		public static extern IntPtr GetCurrentProcess();

		[DllImport("Kernel32", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
		[ResourceExposure(ResourceScope.Process)]
		public static extern IntPtr GetStdHandle(int whichHandle);
	}
}
