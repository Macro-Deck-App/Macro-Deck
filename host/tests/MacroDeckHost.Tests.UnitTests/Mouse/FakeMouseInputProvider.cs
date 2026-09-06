using System.Globalization;
using MacroDeckHost.Integrations.Mouse;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

internal sealed class FakeMouseInputProvider : IMouseInputProvider
{
	public List<string> Calls { get; } = [];

	public string PlatformName => "fake";
	public bool IsSupported { get; set; } = true;
	public bool IsDegraded { get; set; }
	public LocalizedText DegradedReason { get; set; }
	public bool RequiresPermission => false;
	public bool HasPermission => true;

	public MousePoint? CursorPosition { get; set; } = new(100, 100);

	public MouseRect? DesktopBounds { get; set; } = new(0, 0, 1920, 1080);

	public void RequestPermission() => Calls.Add("request-permission");

	public bool TryGetPosition(out MousePoint position)
	{
		position = CursorPosition ?? default;
		return CursorPosition is not null;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		bounds = DesktopBounds ?? default;
		return DesktopBounds is not null;
	}

	public void MoveTo(MousePoint position)
	{
		CursorPosition = position;
		Calls.Add($"move({Format(position)})");
	}

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
		if (position is { } point)
		{
			CursorPosition = point;
		}

		Calls.Add($"down({button}{At(position)})");
	}

	public void ButtonUp(MouseButton button) => Calls.Add($"up({button})");

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
		if (position is { } point)
		{
			CursorPosition = point;
		}

		Calls.Add($"click({button},{clickCount.ToString(CultureInfo.InvariantCulture)}{At(position)})");
	}

	public void DragTo(MouseButton button, MousePoint position)
	{
		CursorPosition = position;
		Calls.Add($"drag({button},{Format(position)})");
	}

	public void Scroll(ScrollAxis axis, int notches)
		=> Calls.Add($"scroll({axis},{notches.ToString(CultureInfo.InvariantCulture)})");

	private static string Format(MousePoint point)
		=> $"{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)}";

	private static string At(MousePoint? position) => position is { } point ? $"@{Format(point)}" : string.Empty;
}
