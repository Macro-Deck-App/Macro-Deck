using MacroDeck.Localization;
using MacroDeck.Sdk.Logging;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Integrations.Mouse.Native;

public sealed class MouseInputService : IMouseInputService
{
	private const int MaxDragSteps = 200;

	private readonly IMouseInputProvider _provider;
	private readonly ILogger _logger;

	private readonly Lock _gate = new();
	private readonly HashSet<MouseButton> _heldButtons = [];

	public MouseInputService(IMouseInputProvider provider, ILogger? logger = null)
	{
		_provider = provider;
		_logger = (logger ?? IntegrationLog.For(MouseInputIntegration.IntegrationId))
			.ForContext<MouseInputService>();
	}

	public bool IsSupported => _provider.IsSupported;

	public bool IsDegraded => _provider.IsDegraded;

	public LocalizedText DegradedReason => _provider.DegradedReason;

	public bool RequiresPermission => _provider.RequiresPermission;

	public bool HasPermission => _provider.HasPermission;

	public bool HasHeldButtons
	{
		get
		{
			lock (_gate)
			{
				return _heldButtons.Count > 0;
			}
		}
	}

	public Task RequestPermissionAsync(CancellationToken cancellationToken = default)
		=> Task.Run(_provider.RequestPermission, cancellationToken);

	public Task MoveAsync(MouseTarget target, CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					if (TryResolve(target, out var point) == TargetResolution.Resolved)
					{
						_provider.MoveTo(point);
					}
				}
			},
			cancellationToken);
	}

	public Task ClickAsync(
		MouseButton button,
		int clickCount = 1,
		MouseTarget target = default,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();

		var clicks = Math.Clamp(clickCount, 1, 3);
		var count = Math.Max(1, repeat);
		var delay = Math.Max(0, repeatDelayMs);

		return Task.Run(async () =>
			{
				for (var i = 0; i < count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					lock (_gate)
					{
						// Only the first repetition moves: repeating a click at the same spot must not
						// re-resolve a relative offset, which would walk the pointer across the screen.
						var position = i == 0 ? ResolveOrSkip(target) : null;
						if (target.Moves && i == 0 && position is null)
						{
							return;
						}

						_provider.Click(button, clicks, position);
					}

					if (i < count - 1 && delay > 0)
					{
						await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
					}
				}
			},
			cancellationToken);
	}

	public Task ButtonDownAsync(
		MouseButton button,
		MouseTarget target = default,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					var position = ResolveOrSkip(target);
					if (target.Moves && position is null)
					{
						return;
					}

					HoldButton(button, position);
				}
			},
			cancellationToken);
	}

	public Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default)
	{
		EnsureSupported();
		return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (_gate)
				{
					ReleaseButton(button);
				}
			},
			cancellationToken);
	}

	public Task DragAsync(
		MouseButton button,
		MouseTarget origin,
		MouseTarget destination,
		int durationMs = 250,
		int steps = 20,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();

		var stepCount = Math.Clamp(steps, 1, MaxDragSteps);
		var duration = Math.Max(0, durationMs);

		return Task.Run(async () =>
			{
				cancellationToken.ThrowIfCancellationRequested();

				MousePoint start;
				lock (_gate)
				{
					if (TryResolveDragStart(origin, out start) != TargetResolution.Resolved)
					{
						return;
					}

					HoldButton(button, start);
				}

				try
				{
					var end = destination.Mode switch
					{
						MouseCoordinateMode.Absolute
							=> Clamp(new MousePoint(destination.X, destination.Y)),
						MouseCoordinateMode.Relative
							=> Clamp(new MousePoint(start.X + destination.X, start.Y + destination.Y)),
						_ => start
					};

					var stepDelay = duration / stepCount;
					for (var step = 1; step <= stepCount; step++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						var progress = (double)step / stepCount;
						var point = new MousePoint(start.X + (int)Math.Round((end.X - start.X) * progress),
							start.Y + (int)Math.Round((end.Y - start.Y) * progress));

						lock (_gate)
						{
							_provider.DragTo(button, point);
						}

						if (stepDelay > 0 && step < stepCount)
						{
							await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
						}
					}
				}
				finally
				{
					lock (_gate)
					{
						ReleaseButton(button);
					}
				}
			},
			cancellationToken);
	}

	public Task ScrollAsync(
		ScrollAxis axis,
		int notches,
		MouseTarget target = default,
		int stepDelayMs = 0,
		CancellationToken cancellationToken = default)
	{
		EnsureSupported();

		if (notches == 0)
		{
			return Task.CompletedTask;
		}

		var delay = Math.Max(0, stepDelayMs);
		var direction = Math.Sign(notches);
		var count = Math.Abs(notches);

		return Task.Run(async () =>
			{
				cancellationToken.ThrowIfCancellationRequested();

				lock (_gate)
				{
					var position = ResolveOrSkip(target);
					if (target.Moves && position is null)
					{
						return;
					}

					if (position is { } point)
					{
						_provider.MoveTo(point);
					}
				}

				for (var i = 0; i < count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					lock (_gate)
					{
						_provider.Scroll(axis, direction);
					}

					if (i < count - 1 && delay > 0)
					{
						await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
					}
				}
			},
			cancellationToken);
	}

	public Task ReleaseAllAsync(CancellationToken cancellationToken = default)
	{
		// Deliberately does not call EnsureSupported and does not observe the token: this also runs
		// from ShutdownAsync, where completing matters more than being cancellable.
		if (!_provider.IsSupported)
		{
			return Task.CompletedTask;
		}

		return Task.Run(() =>
			{
				lock (_gate)
				{
					foreach (var button in _heldButtons.ToArray())
					{
						_provider.ButtonUp(button);
					}

					_heldButtons.Clear();
				}
			},
			cancellationToken);
	}

	private void HoldButton(MouseButton button, MousePoint? position)
	{
		_provider.ButtonDown(button, position);
		_heldButtons.Add(button);
	}

	private void ReleaseButton(MouseButton button)
	{
		if (!_heldButtons.Remove(button))
		{
			return;
		}

		_provider.ButtonUp(button);
	}

	private MousePoint? ResolveOrSkip(MouseTarget target)
		=> TryResolve(target, out var point) == TargetResolution.Resolved ? point : null;

	private TargetResolution TryResolve(MouseTarget target, out MousePoint point)
	{
		point = default;

		switch (target.Mode)
		{
			case MouseCoordinateMode.Absolute:
				point = Clamp(new MousePoint(target.X, target.Y));
				return TargetResolution.Resolved;

			case MouseCoordinateMode.Relative:
				if (!_provider.TryGetPosition(out var current))
				{
					_logger.Warning("Cannot read the cursor position on {Platform}; skipping the relative mouse action",
						_provider.PlatformName);
					return TargetResolution.Unavailable;
				}

				point = Clamp(new MousePoint(current.X + target.X, current.Y + target.Y));
				return TargetResolution.Resolved;

			default:
				return TargetResolution.NoMove;
		}
	}

	private TargetResolution TryResolveDragStart(MouseTarget origin, out MousePoint start)
	{
		if (origin.Mode != MouseCoordinateMode.Current)
		{
			return TryResolve(origin, out start);
		}

		if (_provider.TryGetPosition(out start))
		{
			return TargetResolution.Resolved;
		}

		_logger.Warning("Cannot read the cursor position on {Platform}; skipping the drag",
			_provider.PlatformName);
		return TargetResolution.Unavailable;
	}

	private MousePoint Clamp(MousePoint point)
		=> _provider.TryGetDesktopBounds(out var bounds) ? bounds.Clamp(point) : point;

	private void EnsureSupported()
	{
		if (!_provider.IsSupported)
		{
			throw new PlatformNotSupportedException(
				$"Mouse input simulation is not available on this platform/session ({_provider.PlatformName}).");
		}
	}

	private enum TargetResolution
	{
		NoMove,

		Resolved,

		Unavailable
	}
}
