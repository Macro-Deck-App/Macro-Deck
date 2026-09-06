using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

[SupportedOSPlatform("windows")]
internal static class WindowsCredentialManagerInterop
{
	public const uint CredTypeGeneric = 1;

	/// <summary>Per-user, survives a logoff. Not readable by another Windows account.</summary>
	public const uint CredPersistLocalMachine = 2;

	public const int ErrorNotFound = 1168;

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct Credential
	{
		public uint Flags;
		public uint Type;
		public IntPtr TargetName;
		public IntPtr Comment;
		public long LastWritten;
		public uint CredentialBlobSize;
		public IntPtr CredentialBlob;
		public uint Persist;
		public uint AttributeCount;
		public IntPtr Attributes;
		public IntPtr TargetAlias;
		public IntPtr UserName;
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CredWrite(ref Credential credential, uint flags);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CredDelete(string target, uint type, uint flags);

	[DllImport("advapi32.dll")]
	public static extern void CredFree(IntPtr buffer);
}
