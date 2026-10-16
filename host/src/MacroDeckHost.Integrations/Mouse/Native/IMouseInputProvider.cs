using MacroDeck.Localization;
using MacroDeckHost.Integrations.Mouse.Models;

namespace MacroDeckHost.Integrations.Mouse.Native;

public interface IMouseInputProvider
{
	string PlatformName { get; }

	bool IsSupported { get; }

	bool IsDegraded { get; }

	LocalizedText DegradedReason { get; }

	bool RequiresPermission { get; }

	bool HasPermission { get; }

	void RequestPermission();

	bool TryGetPosition(out MousePoint position);

	bool TryGetDesktopBounds(out MouseRect bounds);

	void MoveTo(MousePoint position);

	void ButtonDown(MouseButton button, MousePoint? position);

	void ButtonUp(MouseButton button);

	void Click(MouseButton button, int clickCount, MousePoint? position);

	void DragTo(MouseButton button, MousePoint position);

	void Scroll(ScrollAxis axis, int notches);
}
