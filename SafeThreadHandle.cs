using System;
using System.Diagnostics;
using System.Security;

using FacebonkClickIDRemoverProxy;

namespace Microsoft.Win32.SafeHandles
{
	[SuppressUnmanagedCodeSecurityAttribute]
	internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		internal SafeThreadHandle() : base(true)
		{
		}

		internal void InitialSetHandle(IntPtr h)
		{
			Debug.Assert(base.IsInvalid, "Safe handle should only be set once");
			base.SetHandle(h);
		}

		override protected bool ReleaseHandle()
		{
			return SafeNativeMethods.CloseHandle(handle);
		}

	}
}
