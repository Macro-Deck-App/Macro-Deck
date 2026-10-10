using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.Keyboard.Native;

internal sealed class MacOsKeyboardLayout
{
	private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const ushort KeyActionDown = 0; // kUCKeyActionDown
	private const uint TranslateNoDeadKeys = 1; // 1 << kUCKeyTranslateNoDeadKeysBit
	private const ushort HighestKeyCode = 127;

	private readonly Lock _gate = new();
	private IntPtr _sourceId;
	private Dictionary<KeyCode, ushort> _letters = [];

	public static Dictionary<KeyCode, ushort> BuildLetterMap(Func<ushort, string?> translate)
	{
		var letters = new Dictionary<KeyCode, ushort>();
		for (ushort keyCode = 0; keyCode <= HighestKeyCode; keyCode++)
		{
			if (translate(keyCode) is [var character] && char.ToUpperInvariant(character) is >= 'A' and <= 'Z' and var upper)
			{
				letters.TryAdd(KeyCode.A + (upper - 'A'), keyCode);
			}
		}

		return letters;
	}

	[SupportedOSPlatform("macos")]
	public bool TryGetLetterKeyCode(KeyCode key, out ushort keyCode)
	{
		keyCode = 0;
		lock (_gate)
		{
			var source = TISCopyCurrentKeyboardLayoutInputSource();
			if (source == IntPtr.Zero)
			{
				return false;
			}

			try
			{
				var sourceId = TISGetInputSourceProperty(source, ReadTisKey("kTISPropertyInputSourceID"));
				if (sourceId == IntPtr.Zero)
				{
					return false;
				}

				if (_sourceId == IntPtr.Zero || !CFEqual(sourceId, _sourceId))
				{
					_letters = BuildLetterMap(source);
					if (_sourceId != IntPtr.Zero)
					{
						MacOsCoreFoundation.CFRelease(_sourceId);
					}

					_sourceId = CFRetain(sourceId);
				}
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(source);
			}

			return _letters.TryGetValue(key, out keyCode);
		}
	}

	[SupportedOSPlatform("macos")]
	public static Dictionary<KeyCode, ushort> BuildLetterMap(IntPtr inputSource)
	{
		var data = TISGetInputSourceProperty(inputSource, ReadTisKey("kTISPropertyUnicodeKeyLayoutData"));
		if (data == IntPtr.Zero)
		{
			return [];
		}

		var layout = CFDataGetBytePtr(data);
		var keyboardType = (uint)LMGetKbdType();
		var buffer = new char[4];
		return BuildLetterMap(keyCode =>
		{
			uint deadKeyState = 0;
			var status = UCKeyTranslate(layout, keyCode, KeyActionDown, 0, keyboardType, TranslateNoDeadKeys,
				ref deadKeyState, (nuint)buffer.Length, out var length, buffer);
			return status == 0 && length > 0 ? new string(buffer, 0, (int)length) : null;
		});
	}

	[SupportedOSPlatform("macos")]
	private static IntPtr ReadTisKey(string symbol) => MacOsAccessibility.ReadGlobalRef(Carbon, symbol);

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
}
