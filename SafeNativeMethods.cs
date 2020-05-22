using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FacebonkClickIDRemoverProxy
{
	class SafeNativeMethods
	{
		[StructLayout(LayoutKind.Sequential)]
		internal class PROCESS_INFORMATION
		{
			// The handles in PROCESS_INFORMATION are initialized in unmanaged functions.
			// We can't use SafeHandle here because Interop doesn't support [out] SafeHandles in structures/classes yet.            
			public IntPtr hProcess = IntPtr.Zero;
			public IntPtr hThread = IntPtr.Zero;
			public int dwProcessId = 0;
			public int dwThreadId = 0;

			// Note this class makes no attempt to free the handles
			// Use InitialSetHandle to copy to handles into SafeHandles
		}

		[DllImport("Kernel32", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
		public static extern bool CloseHandle(IntPtr handle);
	}
}
