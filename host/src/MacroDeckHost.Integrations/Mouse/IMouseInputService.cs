using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Mouse;

public interface IMouseInputService
{
	bool IsSupported { get; }

	bool IsDegraded { get; }

	LocalizedText DegradedReason { get; }

	bool RequiresPermission { get; }

	bool HasPermission { get; }

	bool HasHeldButtons { get; }

	Task RequestPermissionAsync(CancellationToken cancellationToken = default);

	Task MoveAsync(MouseTarget target, CancellationToken cancellationToken = default);

	Task ClickAsync(
		MouseButton button,
		int clickCount = 1,
		MouseTarget target = default,
		int repeat = 1,
		int repeatDelayMs = 0,
		CancellationToken cancellationToken = default);

	Task ButtonDownAsync(
		MouseButton button,
		MouseTarget target = default,
		CancellationToken cancellationToken = default);

	Task ButtonUpAsync(MouseButton button, CancellationToken cancellationToken = default);

	Task DragAsync(
		MouseButton button,
		MouseTarget origin,
		MouseTarget destination,
		int durationMs = 250,
		int steps = 20,
		CancellationToken cancellationToken = default);

	Task ScrollAsync(
		ScrollAxis axis,
		int notches,
		MouseTarget target = default,
		int stepDelayMs = 0,
		CancellationToken cancellationToken = default);

	Task ReleaseAllAsync(CancellationToken cancellationToken = default);
}
