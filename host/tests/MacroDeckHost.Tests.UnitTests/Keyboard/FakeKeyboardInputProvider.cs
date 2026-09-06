using MacroDeckHost.Integrations.Keyboard;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

internal sealed class FakeKeyboardInputProvider : IKeyboardInputProvider
{
	public List<(KeyCode Key, bool Down)> Events { get; } = [];
	public List<string> TypedText { get; } = [];

	public string PlatformName => "fake";
	public bool IsSupported { get; set; } = true;
	public bool RequiresPermission => false;
	public bool HasPermission => true;

	// Targeting (issue #34): configurable so target-mode behaviour can be asserted.
	public bool SupportsWindowTargeting { get; set; }
	public bool SupportsBackgroundSend { get; set; }
	public string? ForegroundProcessName { get; set; }
	public FakeTargetWindow? Target { get; set; }
	public string? ResolvedProcess { get; private set; }

	public void RequestPermission()
	{
	}

	public void KeyDown(KeyCode key) => Events.Add((key, true));

	public void KeyUp(KeyCode key) => Events.Add((key, false));

	public void TypeUnicode(string text) => TypedText.Add(text);

	public string? GetForegroundProcessName() => ForegroundProcessName;

	public IKeyboardTargetWindow? ResolveTarget(string processName)
	{
		ResolvedProcess = processName;
		return Target;
	}
}

internal sealed class FakeTargetWindow : IKeyboardTargetWindow
{
	public List<(KeyCode Key, bool Down)> Events { get; } = [];
	public List<string> TypedText { get; } = [];
	public int FocusCalls { get; private set; }
	public int RestoreCalls { get; private set; }
	public bool CanFocus { get; set; } = true;
	public bool Disposed { get; private set; }

	public IDisposable? Focus()
	{
		FocusCalls++;
		return CanFocus ? new Restore(this) : null;
	}

	public void KeyDown(KeyCode key) => Events.Add((key, true));

	public void KeyUp(KeyCode key) => Events.Add((key, false));

	public void TypeUnicode(string text) => TypedText.Add(text);

	public void Dispose() => Disposed = true;

	private sealed class Restore : IDisposable
	{
		private readonly FakeTargetWindow _owner;

		public Restore(FakeTargetWindow owner)
		{
			_owner = owner;
		}

		public void Dispose() => _owner.RestoreCalls++;
	}
}
