using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.PlatformInput.MacOS;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
[Category("MacOS")]
public sealed class MacOsClipboardTests
{
	private const string OnePixelPng
		= "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

	private readonly MacOsClipboardBackend _backend = new();

	private DirectoryInfo _folder = null!;
	private string? _savedText;

	[SetUp]
	public async Task SaveClipboard()
	{
		_folder = Directory.CreateTempSubdirectory("macrodeck-clipboard-");
		_savedText = (await _backend.ReadTextAsync(int.MaxValue, CancellationToken.None)).Text;
	}

	[TearDown]
	public async Task RestoreClipboard()
	{
		if (_savedText is not null)
		{
			await _backend.SetTextAsync(_savedText, CancellationToken.None);
		}

		_folder.Delete(true);
	}

	[Test]
	public async Task Text_round_trips_through_the_pasteboard_and_a_long_text_reads_as_too_large()
	{
		const string text = "Macro Deck clipboard check\näöü 🎛";

		var receipt = await _backend.SetTextAsync(text, CancellationToken.None);
		var kinds = await _backend.ReadKindsAsync(CancellationToken.None);
		var reading = await _backend.ReadTextAsync(1000, CancellationToken.None);
		var truncated = await _backend.ReadTextAsync(5, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(receipt.ChangeToken, Is.EqualTo(_backend.ReadChangeCount()));
			Assert.That(kinds, Is.EqualTo(ClipboardContentKinds.Text));
			Assert.That(reading.Text, Is.EqualTo(text));
			Assert.That(truncated.IsTooLarge, Is.True);
		});
	}

	[Test]
	public async Task Files_an_image_and_clearing_each_change_the_reported_kinds()
	{
		var file = Path.Combine(_folder.FullName, "notes.txt");
		await File.WriteAllTextAsync(file, "notes");
		var image = Path.Combine(_folder.FullName, "pixel.png");
		await File.WriteAllBytesAsync(image, Convert.FromBase64String(OnePixelPng));
		var fake = Path.Combine(_folder.FullName, "fake.png");
		await File.WriteAllTextAsync(fake, "not an image");

		await _backend.SetFilesAsync([file, _folder.FullName], CancellationToken.None);
		var files = await _backend.ReadKindsAsync(CancellationToken.None);
		await _backend.SetImageAsync(image, CancellationToken.None);
		var picture = await _backend.ReadKindsAsync(CancellationToken.None);
		var refused = Assert.ThrowsAsync<ClipboardException>(()
			=> _backend.SetImageAsync(fake, CancellationToken.None));
		await _backend.ClearAsync(CancellationToken.None);
		var cleared = await _backend.ReadKindsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(files.HasFlag(ClipboardContentKinds.Files), Is.True);
			Assert.That(picture.HasFlag(ClipboardContentKinds.Image), Is.True);
			Assert.That(picture.HasFlag(ClipboardContentKinds.Files), Is.False);
			Assert.That(refused?.Failure, Is.EqualTo(ClipboardFailure.UnsupportedFormat));
			Assert.That(cleared, Is.EqualTo(ClipboardContentKinds.None));
		});
	}

	[Test]
	public async Task The_change_source_reports_a_write_with_its_change_count()
	{
		using var source = _backend.CreateChangeSource();
		var seen = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
		source.Changed += token => seen.TrySetResult(token);
		source.Start();

		var receipt = await _backend.SetTextAsync("watched", CancellationToken.None);

		Assert.That(await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(receipt.ChangeToken));
	}
}
