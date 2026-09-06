using MacroDeckHost.Integrations.Keyboard;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

internal sealed class FakeKeyboardInputService : IKeyboardInputService
{
	public List<string> Calls { get; } = [];

	public bool IsSupported { get; set; } = true;
	public bool RequiresPermission { get; set; }
	public bool HasPermission { get; set; } = true;

	// Targeting (issue #34): when set, OpenSessionAsync yields no session (target not honoured).
	public bool OpenSessionReturnsNull { get; set; }

	public KeyboardSessionUnavailableReason UnavailableReason { get; set; }
		= KeyboardSessionUnavailableReason.NotFocused;

	public KeyboardTarget LastSessionTarget { get; private set; }

	public Task RequestPermissionAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("requestPermission");
		return Task.CompletedTask;
	}

	public Task PressComboAsync(
		KeyModifier modifiers,
		KeyCode key,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default)
	{
		Calls.Add($"combo:{modifiers}+{key}x{repeat}");
		return Task.CompletedTask;
	}

	public Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
	{
		Calls.Add($"text:{text}");
		return Task.CompletedTask;
	}

	public Task KeyDownAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default)
	{
		Calls.Add($"down:{modifiers}+{key}");
		return Task.CompletedTask;
	}

	public Task KeyUpAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default)
	{
		Calls.Add($"up:{modifiers}+{key}");
		return Task.CompletedTask;
	}

	public Task ReleaseAllAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("releaseAll");
		return Task.CompletedTask;
	}

	public Task<KeyboardSessionResult> OpenSessionAsync(
		KeyboardTarget target,
		CancellationToken cancellationToken = default)
	{
		LastSessionTarget = target;
		return Task.FromResult(OpenSessionReturnsNull
			? KeyboardSessionResult.Unavailable(UnavailableReason)
			: KeyboardSessionResult.Opened(new FakeSession(this)));
	}

	private sealed class FakeSession : IKeyboardInputSession
	{
		private readonly FakeKeyboardInputService _owner;

		public FakeSession(FakeKeyboardInputService owner)
		{
			_owner = owner;
		}

		public Task PressComboAsync(
			KeyModifier modifiers,
			KeyCode key,
			int repeat = 1,
			int repeatDelayMs = 0,
			CancellationToken cancellationToken = default)
		{
			_owner.Calls.Add($"combo:{modifiers}+{key}x{repeat}");
			return Task.CompletedTask;
		}

		public Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
		{
			_owner.Calls.Add($"text:{text}");
			return Task.CompletedTask;
		}

		public void Dispose()
		{
		}
	}
}
