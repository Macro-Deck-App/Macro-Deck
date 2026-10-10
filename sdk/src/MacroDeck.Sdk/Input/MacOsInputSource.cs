using System.Runtime.InteropServices;

namespace MacroDeck.Sdk.Input;

internal static class MacOsInputSource
{
	private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	private const ushort KeyActionDown = 0; // kUCKeyActionDown
	private const uint TranslateNoDeadKeys = 1; // 1 << kUCKeyTranslateNoDeadKeysBit
	private const int KeyCodeCount = 128;

	private static readonly Lock _gate = new();
	private static IntPtr _sourceId;
	private static string?[] _characters = new string?[KeyCodeCount];

	public static string? Translate(ushort keyCode)
	{
		if (!OperatingSystem.IsMacOS() || keyCode >= KeyCodeCount)
		{
			return null;
		}

		try
		{
			lock (_gate)
			{
				return Refresh() ? _characters[keyCode] : null;
			}
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
			return null;
		}
	}

	private static bool Refresh()
	{
		var source = TISCopyCurrentKeyboardLayoutInputSource();
		if (source == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			var sourceId = TISGetInputSourceProperty(source, ReadGlobal(Carbon, "kTISPropertyInputSourceID"));
			if (sourceId == IntPtr.Zero)
			{
				return false;
			}

			if (_sourceId == IntPtr.Zero || !CFEqual(sourceId, _sourceId))
			{
				_characters = ReadCharacters(source);
				if (_sourceId != IntPtr.Zero)
				{
					CFRelease(_sourceId);
				}

				_sourceId = CFRetain(sourceId);
			}

			return true;
		}
		finally
		{
			CFRelease(source);
		}
	}

	private static string?[] ReadCharacters(IntPtr source)
	{
		var characters = new string?[KeyCodeCount];
		var data = TISGetInputSourceProperty(source, ReadGlobal(Carbon, "kTISPropertyUnicodeKeyLayoutData"));
		if (data == IntPtr.Zero)
		{
			return characters;
		}

		var layout = CFDataGetBytePtr(data);
		var keyboardType = (uint)LMGetKbdType();
		var buffer = new char[4];
		for (ushort keyCode = 0; keyCode < KeyCodeCount; keyCode++)
		{
			uint deadKeyState = 0;
			var status = UCKeyTranslate(layout, keyCode, KeyActionDown, 0, keyboardType, TranslateNoDeadKeys,
				ref deadKeyState, (nuint)buffer.Length, out var length, buffer);
			characters[keyCode] = status == 0 && length > 0 ? new string(buffer, 0, (int)length) : null;
		}

		return characters;
	}

	private static IntPtr ReadGlobal(string library, string symbol)
		=> NativeLibrary.TryLoad(library, out var handle) && NativeLibrary.TryGetExport(handle, symbol, out var address)
			? Marshal.ReadIntPtr(address)
			: IntPtr.Zero;

	[DllImport(Carbon)]
	private static extern IntPtr TISCopyCurrentKeyboardLayoutInputSource();

	[DllImport(Carbon)]
	private static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

	[DllImport(Carbon)]
	private static extern byte LMGetKbdType();

	[DllImport(Carbon)]
	private static extern int UCKeyTranslate(
		IntPtr keyLayout,
		ushort virtualKeyCode,
		ushort keyAction,
		uint modifierKeyState,
		uint keyboardType,
		uint keyTranslateOptions,
		ref uint deadKeyState,
		nuint maxStringLength,
		out nuint actualStringLength,
		[Out] char[] unicodeString);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFDataGetBytePtr(IntPtr data);

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFEqual(IntPtr first, IntPtr second);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFRetain(IntPtr value);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr value);
}
