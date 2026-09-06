using System.Globalization;
using MacroDeckHost.Integrations.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class FakeAdbGateway : IAdbGateway
{
	public bool IsEnabled { get; set; } = true;

	public IReadOnlyList<AdbGatewayDevice> Devices { get; set; } = [];

	public string? DefaultDeviceSerial { get; set; }

	public AdbGatewayResult NextResult { get; set; } = AdbGatewayResult.Ok();

	public AdbGatewayProperties? NextProperties { get; set; }

	public byte[]? NextScreenshot { get; set; } = [1, 2, 3];

	public List<string> Calls { get; } = [];

	public event EventHandler<AdbGatewayDeviceChange>? DeviceChanged;

	public Task<AdbGatewayResult> SendKeyAsync(string? serial, AdbGatewayKey key, CancellationToken cancellationToken)
	{
		Calls.Add($"SendKey:{serial}:{key}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> StartAppAsync(string? serial, string package, CancellationToken cancellationToken)
	{
		Calls.Add($"StartApp:{serial}:{package}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> ForceStopAppAsync(string? serial, string package, CancellationToken cancellationToken)
	{
		Calls.Add($"ForceStopApp:{serial}:{package}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> OpenUriAsync(string? serial, string uri, CancellationToken cancellationToken)
	{
		Calls.Add($"OpenUri:{serial}:{uri}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> InputTextAsync(string? serial, string text, CancellationToken cancellationToken)
	{
		Calls.Add($"InputText:{serial}:{text}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> TapAsync(string? serial, int x, int y, CancellationToken cancellationToken)
	{
		Calls.Add(
			$"Tap:{serial}:{x.ToString(CultureInfo.InvariantCulture)}:{y.ToString(CultureInfo.InvariantCulture)}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> SwipeAsync(
		string? serial,
		int x1,
		int y1,
		int x2,
		int y2,
		int durationMs,
		CancellationToken cancellationToken)
	{
		Calls.Add(
			$"Swipe:{serial}:{x1.ToString(CultureInfo.InvariantCulture)}:{y1.ToString(CultureInfo.InvariantCulture)}:" +
			$"{x2.ToString(CultureInfo.InvariantCulture)}:{y2.ToString(CultureInfo.InvariantCulture)}:" +
			$"{durationMs.ToString(CultureInfo.InvariantCulture)}");
		return Task.FromResult(NextResult);
	}

	public Task<AdbGatewayResult> RebootAsync(
		string? serial,
		AdbGatewayRebootMode mode,
		CancellationToken cancellationToken)
	{
		Calls.Add($"Reboot:{serial}:{mode}");
		return Task.FromResult(NextResult);
	}

	public Task<(AdbGatewayResult Result, byte[]? Png)> CaptureScreenshotAsync(
		string? serial,
		CancellationToken cancellationToken)
	{
		Calls.Add($"CaptureScreenshot:{serial}");
		return Task.FromResult((NextResult, NextResult.Success ? NextScreenshot : null));
	}

	public Task<AdbGatewayProperties?> GetPropertiesAsync(string? serial, CancellationToken cancellationToken)
	{
		Calls.Add($"GetProperties:{serial}");
		return Task.FromResult(NextProperties);
	}

	public void RaiseDeviceChanged(AdbGatewayDeviceChangeKind kind, AdbGatewayDevice device)
		=> DeviceChanged?.Invoke(this, new AdbGatewayDeviceChange(kind, device));
}
