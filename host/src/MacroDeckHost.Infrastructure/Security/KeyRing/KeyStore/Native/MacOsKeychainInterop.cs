using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

[SupportedOSPlatform("macos")]
internal static class MacOsKeychainInterop
{
	public const int ErrSecSuccess = 0;
	public const int ErrSecItemNotFound = -25300;
	public const int ErrSecDuplicateItem = -25299;

	private const string CoreFoundation =
		"/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	private const string Security = "/System/Library/Frameworks/Security.framework/Security";

	private const uint KCFStringEncodingUtf8 = 0x08000100;

	public static IntPtr SecClass { get; } = ReadGlobal(Security, "kSecClass");
	public static IntPtr SecClassGenericPassword { get; } = ReadGlobal(Security, "kSecClassGenericPassword");
	public static IntPtr SecAttrService { get; } = ReadGlobal(Security, "kSecAttrService");
	public static IntPtr SecAttrAccount { get; } = ReadGlobal(Security, "kSecAttrAccount");
	public static IntPtr SecValueData { get; } = ReadGlobal(Security, "kSecValueData");
	public static IntPtr SecReturnData { get; } = ReadGlobal(Security, "kSecReturnData");
	public static IntPtr SecMatchLimit { get; } = ReadGlobal(Security, "kSecMatchLimit");
	public static IntPtr SecMatchLimitOne { get; } = ReadGlobal(Security, "kSecMatchLimitOne");

	public static IntPtr CFBooleanTrue { get; } = ReadGlobal(CoreFoundation, "kCFBooleanTrue");

	private static IntPtr TypeDictionaryKeyCallBacks { get; } =
		ReadGlobalAddress(CoreFoundation, "kCFTypeDictionaryKeyCallBacks");

	private static IntPtr TypeDictionaryValueCallBacks { get; } =
		ReadGlobalAddress(CoreFoundation, "kCFTypeDictionaryValueCallBacks");

	// Every symbol the store actually dereferences. Omitting one would let a query be built with a null
	// dictionary key, which Core Foundation answers by crashing the process rather than returning an
	// error.
	public static bool IsLoadable =>
		SecClass != IntPtr.Zero &&
		SecClassGenericPassword != IntPtr.Zero &&
		SecAttrService != IntPtr.Zero &&
		SecAttrAccount != IntPtr.Zero &&
		SecValueData != IntPtr.Zero &&
		SecReturnData != IntPtr.Zero &&
		SecMatchLimit != IntPtr.Zero &&
		SecMatchLimitOne != IntPtr.Zero &&
		CFBooleanTrue != IntPtr.Zero &&
		TypeDictionaryKeyCallBacks != IntPtr.Zero &&
		TypeDictionaryValueCallBacks != IntPtr.Zero;

	public static IntPtr CreateString(string value)
		=> CFStringCreateWithCString(IntPtr.Zero,
			System.Text.Encoding.UTF8.GetBytes(value + "\0"),
			KCFStringEncodingUtf8);

	public static IntPtr CreateData(ReadOnlySpan<byte> value)
	{
		var buffer = Marshal.AllocHGlobal(value.Length);
		try
		{
			Marshal.Copy(value.ToArray(), 0, buffer, value.Length);

			return CFDataCreate(IntPtr.Zero, buffer, value.Length);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	public static byte[]? CopyData(IntPtr data)
	{
		if (data == IntPtr.Zero)
		{
			return null;
		}

		var length = (int)CFDataGetLength(data);
		var pointer = CFDataGetBytePtr(data);
		if (length <= 0 || pointer == IntPtr.Zero)
		{
			return null;
		}

		var buffer = new byte[length];
		Marshal.Copy(pointer, buffer, 0, length);

		return buffer;
	}

	public static IntPtr CreateDictionary(IntPtr[] keys, IntPtr[] values)
		=> CFDictionaryCreate(IntPtr.Zero,
			keys,
			values,
			keys.Length,
			TypeDictionaryKeyCallBacks,
			TypeDictionaryValueCallBacks);

	private static IntPtr ReadGlobal(string library, string symbol)
	{
		var address = ReadGlobalAddress(library, symbol);

		return address == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(address);
	}

	private static IntPtr ReadGlobalAddress(string library, string symbol)
		=> NativeLibrary.TryLoad(library, out var handle) &&
			NativeLibrary.TryGetExport(handle, symbol, out var address)
				? address
				: IntPtr.Zero;

	[DllImport(Security)]
	public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

	[DllImport(Security)]
	public static extern int SecItemAdd(IntPtr attributes, IntPtr result);

	[DllImport(Security)]
	public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

	[DllImport(Security)]
	public static extern int SecItemDelete(IntPtr query);

	[DllImport(CoreFoundation)]
	public static extern void CFRelease(IntPtr handle);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] bytes, uint encoding);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDataCreate(IntPtr allocator, IntPtr bytes, long length);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDataGetBytePtr(IntPtr data);

	[DllImport(CoreFoundation)]
	private static extern long CFDataGetLength(IntPtr data);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDictionaryCreate(IntPtr allocator,
		IntPtr[] keys,
		IntPtr[] values,
		long count,
		IntPtr keyCallBacks,
		IntPtr valueCallBacks);
}
