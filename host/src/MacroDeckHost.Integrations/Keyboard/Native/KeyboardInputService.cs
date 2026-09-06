using MacroDeck.Sdk.Logging;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Keyboard.Native;

public sealed class KeyboardInputService : IKeyboardInputService
{
	private readonly IKeyboardInputProvider _provider;
	private readonly IKeyboardLayoutService _layout;
	private readonly ILogger _logger;

	private readonly Lock _gate = new();
	private readonly HashSet<KeyCode> _heldKeys = [];

	public KeyboardInputService(IKeyboardInputProvider provider, IKeyboardLayoutService layout, ILogger? logger = null)
	{
		_provider = provider;
		_layout = layout;
		_logger =
			(logger ?? IntegrationLog.For(KeyboardInputIntegration.IntegrationId)).ForContext<KeyboardInputService>();
	}

	public bool IsSupported => _provider.IsSupported;

	public bool RequiresPermission => _provider.RequiresPermission;

	public bool HasPermission => _provider.HasPermission;

	private Emitter GlobalEmitter => new(_provider.KeyDown, _provider.KeyUp, _provider.TypeUnicode);

	public Task RequestPermissionAsync(CancellationToken cancellationToken = default)
		=> Task.Run(_provider.RequestPermission, cancellationToken);

	public Task PressComboAsync(
		KeyModifier modifiers,
		KeyCode key,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return RunPressComboAsync(GlobalEmitter, modifiers, key, repeat, repeatDelayMs, cancellationToken);
	}

	public Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return RunTypeTextAsync(GlobalEmitter, text, cancellationToken);
	}

	public Task KeyDownAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					foreach (var modifierKey in _layout.ExpandModifiers(modifiers))
					{
						HoldKey(modifierKey);
					}

					HoldKey(key);
				}
			},
			cancellationToken);
	}

	public Task KeyUpAsync(KeyModifier modifiers, KeyCode key, CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					ReleaseKey(key);
					foreach (var modifierKey in _layout.ExpandModifiers(modifiers))
					{
						ReleaseKey(modifierKey);
					}
				}
			},
			cancellationToken);
	}

	public Task ReleaseAllAsync(CancellationToken cancellationToken = default)
	{
		if (!_provider.IsSupported)
		{
			return Task.CompletedTask;
		}

		return Task.Run(() =>
			{
				lock (_gate)
				{
					foreach (var key in _heldKeys.ToArray())
					{
						_provider.KeyUp(key);
					}

					_heldKeys.Clear();
				}
			},
			cancellationToken);
	}

	public Task<KeyboardSessionResult> OpenSessionAsync(
		KeyboardTarget target,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() => OpenSession(target), cancellationToken);
	}

	private KeyboardSessionResult OpenSession(KeyboardTarget target)
	{
		if (!target.HasProcess)
		{
			return KeyboardSessionResult.Opened(new Session(this, GlobalEmitter, window: null, focus: null));
		}

		switch (target.Mode)
		{
			case KeyboardTargetMode.WhenFocused:
			{
				if (!_provider.SupportsWindowTargeting)
				{
					WarnUnsupported(target, "only-when-focused");
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.Unsupported);
				}

				var foreground = _provider.GetForegroundProcessName();
				return KeyboardProcessName.Matches(foreground, target.ProcessName)
					? KeyboardSessionResult.Opened(new Session(this, GlobalEmitter, window: null, focus: null))
					: KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.NotFocused);
			}

			case KeyboardTargetMode.FocusThenSend:
			{
				if (!_provider.SupportsWindowTargeting)
				{
					WarnUnsupported(target, "focus-then-send");
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.Unsupported);
				}

				var window = _provider.ResolveTarget(target.ProcessName);
				if (window is null)
				{
					DebugUnresolved(target);
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.TargetNotFound);
				}

				var focus = window.Focus();
				if (focus is null)
				{
					window.Dispose();
					_logger.Warning(
						"Found a window for keyboard target process {Process} but could not bring it to the foreground; skipping",
						target.ProcessName);
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.FocusFailed);
				}

				return KeyboardSessionResult.Opened(new Session(this, GlobalEmitter, window, focus));
			}

			case KeyboardTargetMode.Background:
			{
				if (!_provider.SupportsBackgroundSend)
				{
					WarnUnsupported(target, "background");
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.Unsupported);
				}

				var window = _provider.ResolveTarget(target.ProcessName);
				if (window is null)
				{
					DebugUnresolved(target);
					return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.TargetNotFound);
				}

				var emitter = new Emitter(window.KeyDown, window.KeyUp, window.TypeUnicode);
				return KeyboardSessionResult.Opened(new Session(this, emitter, window, focus: null));
			}

			default:
				return KeyboardSessionResult.Unavailable(KeyboardSessionUnavailableReason.Unsupported);
		}
	}

	private Task RunPressComboAsync(
		Emitter emitter,
		KeyModifier modifiers,
		KeyCode key,
		int repeat,
		int repeatDelayMs,
		CancellationToken cancellationToken)
	{
		var count = Math.Max(1, repeat);
		var delay = Math.Max(0, repeatDelayMs);

		return Task.Run(async () =>
			{
				var modifierKeys = _layout.ExpandModifiers(modifiers);
				for (var i = 0; i < count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					PressCombo(emitter, modifierKeys, key);

					if (i < count - 1 && delay > 0)
					{
						await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
					}
				}
			},
			cancellationToken);
	}

	private Task RunTypeTextAsync(Emitter emitter, string text, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(text))
		{
			return Task.CompletedTask;
		}

		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					emitter.Type(text);
				}
			},
			cancellationToken);
	}

	private void PressCombo(Emitter emitter, IReadOnlyList<KeyCode> modifierKeys, KeyCode key)
	{
		lock (_gate)
		{
			foreach (var modifierKey in modifierKeys)
			{
				emitter.Down(modifierKey);
			}

			if (key != KeyCode.None)
			{
				emitter.Down(key);
				emitter.Up(key);
			}

			for (var i = modifierKeys.Count - 1; i >= 0; i--)
			{
				emitter.Up(modifierKeys[i]);
			}
		}
	}

	private void HoldKey(KeyCode key)
	{
		if (key == KeyCode.None)
		{
			return;
		}

		_provider.KeyDown(key);
		_heldKeys.Add(key);
	}

	private void ReleaseKey(KeyCode key)
	{
		if (key == KeyCode.None || !_heldKeys.Remove(key))
		{
			return;
		}

		_provider.KeyUp(key);
	}

	private void WarnUnsupported(KeyboardTarget target, string mode)
		=> _logger.Warning(
			"Keyboard target mode '{Mode}' is not supported on {Platform}; skipping keystroke to {Process}",
			mode,
			_provider.PlatformName,
			target.ProcessName);

	private void DebugUnresolved(KeyboardTarget target)
		=> _logger.Debug("No window found for keyboard target process {Process}; skipping", target.ProcessName);

	private void EnsureSupported()
	{
		if (!_provider.IsSupported)
		{
			throw new PlatformNotSupportedException(
				$"Keyboard input simulation is not available on this platform/session ({_provider.PlatformName}).");
		}
	}

	private readonly record struct Emitter(Action<KeyCode> Down, Action<KeyCode> Up, Action<string> Type);

	private sealed class Session : IKeyboardInputSession
	{
		private readonly KeyboardInputService _owner;
		private readonly Emitter _emitter;
		private readonly IKeyboardTargetWindow? _window;
		private readonly IDisposable? _focus;

		public Session(
			KeyboardInputService owner,
			Emitter emitter,
			IKeyboardTargetWindow? window,
			IDisposable? focus)
		{
			_owner = owner;
			_emitter = emitter;
			_window = window;
			_focus = focus;
		}

		public Task PressComboAsync(
			KeyModifier modifiers,
			KeyCode key,
			int repeat = 1,
			int repeatDelayMs = 0,
			CancellationToken cancellationToken = default)
			=> _owner.RunPressComboAsync(_emitter, modifiers, key, repeat, repeatDelayMs, cancellationToken);

		public Task TypeTextAsync(string text, CancellationToken cancellationToken = default)
			=> _owner.RunTypeTextAsync(_emitter, text, cancellationToken);

		public void Dispose()
		{
			_focus?.Dispose();
			_window?.Dispose();
		}
	}
}
