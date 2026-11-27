using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Clipboard;

public abstract class ClipboardBackendCases
{
	protected const string OnePixelPng
		= "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

	private string? _savedText;

	internal IClipboardBackend Backend { get; private set; } = null!;

	protected DirectoryInfo Folder { get; private set; } = null!;

	internal abstract IClipboardBackend CreateBackend();

	[OneTimeSetUp]
	public void CreateClipboardBackend()
	{
		Backend = CreateBackend();
		Interactive.Require(Backend.Availability.IsAvailable,
			$"The clipboard is unavailable here: {Backend.Availability.Reason} {Backend.Availability.MissingTool}");
	}

	[SetUp]
	public async Task SaveClipboard()
	{
		Folder = Directory.CreateTempSubdirectory("macrodeck-clipboard-");
		try
		{
			_savedText = (await Backend.ReadTextAsync(64 * 1024, CancellationToken.None)).Text;
		}
		catch (ClipboardException)
		{
			_savedText = null;
		}
	}

	[TearDown]
	public async Task RestoreClipboard()
	{
		try
		{
			if (_savedText is not null)
			{
				await Backend.SetTextAsync(_savedText, CancellationToken.None);
			}
		}
		finally
		{
			Folder.Delete(true);
		}
	}

	[Test]
	public async Task Text_round_trips_through_the_clipboard_and_a_long_text_reads_as_too_large()
	{
		const string text = "Macro Deck clipboard check\näöü 🎛";

		await Backend.SetTextAsync(text, CancellationToken.None);
		var kinds = await Backend.ReadKindsAsync(CancellationToken.None);
		var reading = await Backend.ReadTextAsync(1000, CancellationToken.None);
		var truncated = await Backend.ReadTextAsync(5, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(kinds.HasFlag(ClipboardContentKinds.Text), Is.True, kinds.ToString());
			Assert.That(reading.Text, Is.EqualTo(text));
			Assert.That(truncated.IsTooLarge, Is.True);
		});
	}

	[Test]
	public async Task Files_an_image_and_clearing_each_change_the_reported_kinds()
	{
		var file = Path.Combine(Folder.FullName, "notes with space.txt");
		await File.WriteAllTextAsync(file, "notes");
		var image = Path.Combine(Folder.FullName, "pixel.png");
		await File.WriteAllBytesAsync(image, Convert.FromBase64String(OnePixelPng));
		var fake = Path.Combine(Folder.FullName, "fake.png");
		await File.WriteAllTextAsync(fake, "not an image");

		await Backend.SetFilesAsync([file, Folder.FullName], CancellationToken.None);
		var files = await Backend.ReadKindsAsync(CancellationToken.None);
		await Backend.SetImageAsync(image, CancellationToken.None);
		var picture = await Backend.ReadKindsAsync(CancellationToken.None);
		var refused = Assert.ThrowsAsync<ClipboardException>(() => Backend.SetImageAsync(fake, CancellationToken.None));
		await Backend.ClearAsync(CancellationToken.None);
		var cleared = await Backend.ReadKindsAsync(CancellationToken.None);
		var clearedText = await Backend.ReadTextAsync(1000, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(files.HasFlag(ClipboardContentKinds.Files), Is.True, files.ToString());
			Assert.That(picture.HasFlag(ClipboardContentKinds.Image), Is.True, picture.ToString());
			Assert.That(picture.HasFlag(ClipboardContentKinds.Files), Is.False, picture.ToString());
			Assert.That(refused?.Failure, Is.EqualTo(ClipboardFailure.UnsupportedFormat));
			Assert.That(cleared, Is.EqualTo(ClipboardContentKinds.None));
			Assert.That(clearedText.HasText, Is.False);
		});
	}
}
