using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
public class JsonKnownAudioDeviceStoreTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private JsonKnownAudioDeviceStore CreateStore() => new(_paths, new LoggerConfiguration().CreateLogger());

	[Test]
	public void An_empty_data_directory_loads_as_an_empty_list()
	{
		var loaded = CreateStore().TryLoad(out var devices);

		Assert.Multiple(() =>
		{
			Assert.That(loaded, Is.True);
			Assert.That(devices, Is.Empty);
		});
	}

	[Test]
	public void Saved_devices_are_loaded_by_a_new_store_instance()
	{
		KnownAudioDevice[] saved =
		[
			new("output", "speakers-uid", "0a1b2c3d", "macbook_pro_speakers", "MacBook Pro Speakers"),
			new("input", "mic-uid", "4e5f6a7b", "macbook_pro_microphone", "MacBook Pro Microphone")
		];

		Assert.That(CreateStore().Save(saved), Is.True);
		var loaded = CreateStore().TryLoad(out var devices);

		Assert.Multiple(() =>
		{
			Assert.That(loaded, Is.True);
			Assert.That(devices, Is.EqualTo(saved));
		});
	}

	[Test]
	public void An_unreadable_file_is_reported_rather_than_read_as_empty()
	{
		Directory.CreateDirectory(_paths.DataDirectory);
		File.WriteAllText(Path.Combine(_paths.DataDirectory, "system-audio-devices.json"), "{ not json");

		Assert.That(CreateStore().TryLoad(out _), Is.False);
	}
}
