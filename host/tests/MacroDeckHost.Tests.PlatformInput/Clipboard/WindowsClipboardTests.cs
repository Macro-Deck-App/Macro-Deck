using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.PlatformInput.Clipboard;

[Platform("Win")]
[Category("Windows")]
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardTests : ClipboardBackendCases
{
	internal override IClipboardBackend CreateBackend()
	{
		return new WindowsClipboardBackend();
	}

	[Test]
	public async Task A_write_carries_the_sequence_number_the_change_listener_then_reports()
	{
		using var source = Backend.CreateChangeSource();
		var seen = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
		source.Changed += changeToken => seen.TrySetResult(changeToken);
		source.Start();
		await Task.Delay(200);

		var receipt = await Backend.SetTextAsync("watched", CancellationToken.None);
		var token = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(receipt.ChangeToken, Is.Not.Null);
			Assert.That(token, Is.EqualTo(receipt.ChangeToken));
			Assert.That(source.IsWatchUnsupported, Is.False);
		});
	}

	[Test]
	public async Task A_png_is_offered_as_png_and_as_a_bitmap()
	{
		var image = Path.Combine(Folder.FullName, "pixel.png");
		await File.WriteAllBytesAsync(image, Convert.FromBase64String(OnePixelPng));

		await Backend.SetImageAsync(image, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(WindowsClipboardNative.IsClipboardFormatAvailable(WindowsClipboardNative.CfBitmap), Is.True);
			Assert.That(WindowsClipboardNative.IsClipboardFormatAvailable(
					WindowsClipboardNative.RegisterClipboardFormatW("PNG")),
				Is.True);
		});
	}
}
