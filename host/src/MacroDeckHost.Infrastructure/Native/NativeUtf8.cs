using System.Runtime.InteropServices;
using System.Text;

namespace MacroDeckHost.Infrastructure.Native;

internal static class NativeUtf8
{
	public static IntPtr Alloc(string value)
	{
		var bytes = Encoding.UTF8.GetBytes(value + "\0");
		var buffer = Marshal.AllocHGlobal(bytes.Length);
		Marshal.Copy(bytes, 0, buffer, bytes.Length);

		return buffer;
	}

	public static void Free(IntPtr buffer)
	{
		if (buffer != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(buffer);
		}
	}
}
