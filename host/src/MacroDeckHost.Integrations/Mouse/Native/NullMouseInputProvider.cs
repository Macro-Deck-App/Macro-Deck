using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Mouse.Native;

public sealed class NullMouseInputProvider : IMouseInputProvider
{
	private readonly string _reason;

	public NullMouseInputProvider(string reason)
	{
		_reason = reason;
	}

	public string PlatformName => $"unsupported ({_reason})";

	public bool IsSupported => false;

	public bool IsDegraded => false;

	public LocalizedText DegradedReason => default;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public bool TryGetPosition(out MousePoint position)
	{
		position = default;
		return false;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		bounds = default;
		return false;
	}

	public void MoveTo(MousePoint position)
	{
	}

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
	}

	public void ButtonUp(MouseButton button)
	{
	}

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
	}

	public void DragTo(MouseButton button, MousePoint position)
	{
	}

	public void Scroll(ScrollAxis axis, int notches)
	{
	}
}
