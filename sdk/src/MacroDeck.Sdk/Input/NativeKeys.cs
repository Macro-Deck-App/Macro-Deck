namespace MacroDeck.Sdk.Input;

/// <summary>
/// Translates the key codes a native keyboard hook delivers into <see cref="KeyCode" /> values, so a plugin can
/// publish the exact names the combo editor stores (see <see cref="KeyNames.ToName" />). A key that has no
/// <see cref="KeyCode" />, or a number that is no key code at all, returns <see cref="KeyCode.None" />; the
/// methods never throw and are safe to call from any thread.
/// </summary>
/// <remarks>
/// Letters <c>A</c> to <c>Z</c> name the key labeled with that letter on the active layout. Digits and punctuation
/// name a physical position, spelled like the browser's <c>KeyboardEvent.code</c>.
/// </remarks>
public static class NativeKeys
{
	/// <summary>
	/// Translates a Windows virtual-key code. <paramref name="isExtended" /> is the extended-key flag of the event
	/// (<c>LLKHF_EXTENDED</c>): it tells <see cref="KeyCode.NumpadEnter" /> from <see cref="KeyCode.Enter" />, and a
	/// navigation key from the numpad key that reports the same virtual key while NumLock is off. A caller that
	/// does not have the flag (RegisterHotKey, GetAsyncKeyState) passes what it knows.
	/// </summary>
	/// <remarks>
	/// Punctuation virtual keys (<c>VK_OEM_*</c>) follow the active layout, so they are mapped to their position
	/// through the foreground window's layout. Prefer the overload that takes the scan code.
	/// </remarks>
	public static KeyCode FromWindows(int virtualKey, bool isExtended)
		=> NativeKeyTables.FromWindows(virtualKey, 0, isExtended, WindowsKeyboardLayout.ScanCodeOf);

	/// <summary>
	/// Translates a Windows virtual-key code with the scan code of the event (<c>KBDLLHOOKSTRUCT.scanCode</c>, the
	/// 8-bit code without the extended prefix, which is <paramref name="isExtended" />). The scan code names the
	/// position of a punctuation key exactly, so no layout lookup is needed; a scan code of 0 or one that is
	/// unknown falls back to <see cref="FromWindows(int, bool)" />.
	/// </summary>
	public static KeyCode FromWindows(int virtualKey, int scanCode, bool isExtended)
		=> NativeKeyTables.FromWindows(virtualKey, scanCode, isExtended, WindowsKeyboardLayout.ScanCodeOf);

	/// <summary>
	/// Translates a macOS key code (<c>kVK_*</c>, <c>kCGKeyboardEventKeycode</c>). The code is a US key position, so
	/// a letter is resolved through the active input source: on a German layout code 6 is <see cref="KeyCode.Y" />.
	/// A letter position whose key types no Latin letter (Cyrillic, Dvorak punctuation) keeps its US letter. Code 10
	/// is <see cref="KeyCode.IntlBackslash" /> and 50 is <see cref="KeyCode.Backquote" />, as WebKit reports them.
	/// </summary>
	/// <remarks>
	/// Off macOS, or when the input source cannot be read, letters follow US positions. The input source is read
	/// with the Text Input Sources API, which Apple documents for the main thread.
	/// </remarks>
	public static KeyCode FromMacOS(int keyCode)
		=> NativeKeyTables.FromMacOS(keyCode, MacOsInputSource.Translate);

	/// <summary>
	/// Translates a Linux evdev code (<c>KEY_*</c> from <c>input-event-codes.h</c>). An evdev code names a
	/// position, so letters follow US positions on every layout: on a German layout the Z-labeled key is
	/// <see cref="KeyCode.Y" /> here.
	/// </summary>
	public static KeyCode FromLinux(int evdevCode) => NativeKeyTables.FromLinux(evdevCode);
}
