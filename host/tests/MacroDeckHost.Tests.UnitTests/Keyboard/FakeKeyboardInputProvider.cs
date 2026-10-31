using MacroDeck.Localization;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

internal sealed class FakeKeyboardInputProvider : IKeyboardInputProvider
{
	public List<(KeyCode Key, bool Down)> Events { get; } = [];
	public List<string> TypedText { get; } = [];

	public string PlatformName => "fake";
	public bool IsSupported { get; set; } = true;
	public bool IsDegraded { get; set; }
	public LocalizedText DegradedReason { get; set; }
	public bool RequiresPermission => false;
	public bool HasPermission => true;

	// Targeting (issue #34): configurable so target-mode behaviour can be asserted.
	public bool SupportsWindowTargeting { get; set; }
	public bool SupportsBackgroundSend { get; set; }

	public KeyModifier BackgroundModifiers { get; set; }
		= KeyModifier.Control | KeyModifier.Shift | KeyModifier.Alt | KeyModifier.Meta;

	public string? ForegroundProcessName { get; set; }
	public FakeTargetWindow? Target { get; set; }
	public string? ResolvedProcess { get; private set; }
	public int ResolveCount { get; private set; }

	public void RequestPermission()
	{
	}

	public KeyCode? FailOnKeyDown { get; set; }

	public Action<KeyCode>? AfterKeyDown { get; set; }

	public List<string>? Timeline { get; set; }

	// Keys down and not yet up, as the operating system would see them.
	public IReadOnlyCollection<KeyCode> Pressed
		=> Events.GroupBy(e => e.Key).Where(g => g.Last().Down).Select(g => g.Key).ToList();

	public void KeyDown(KeyCode key)
	{
		if (key == FailOnKeyDown)
		{
			throw new InvalidOperationException($"The platform refused {key}.");
		}

		Events.Add((key, true));
		Timeline?.Add($"down:{key}");
		AfterKeyDown?.Invoke(key);
	}

	public void KeyUp(KeyCode key)
	{
		Events.Add((key, false));
		Timeline?.Add($"up:{key}");
	}

	public void TypeUnicode(string text)
	{
		TypedText.Add(text);
	}

	public string? GetForegroundProcessName()
	{
		return ForegroundProcessName;
	}

	public IKeyboardTargetWindow? ResolveTarget(string processName)
	{
		ResolvedProcess = processName;
		ResolveCount++;
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
	public List<string>? Timeline { get; set; }

	public IDisposable? Focus()
	{
		FocusCalls++;
		return CanFocus ? new Restore(this) : null;
	}

	public void KeyDown(KeyCode key)
	{
		Events.Add((key, true));
	}

	public void KeyUp(KeyCode key)
	{
		Events.Add((key, false));
	}

	public void TypeUnicode(string text)
	{
		TypedText.Add(text);
	}

	public void Dispose()
	{
		Disposed = true;
	}

	private sealed class Restore : IDisposable
	{
		private readonly FakeTargetWindow _owner;

		public Restore(FakeTargetWindow owner)
		{
			_owner = owner;
		}

		public void Dispose()
		{
			_owner.RestoreCalls++;
			_owner.Timeline?.Add("restore-focus");
		}
	}
}
