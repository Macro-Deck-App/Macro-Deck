using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class LoopbackPortFilesTests
{
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("md-port-files-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_directory, true);
	}

	[Test]
	public async Task An_old_cli_keeps_finding_the_port_in_the_legacy_file_beside_the_per_user_file()
	{
		var perUser = Path.Combine(_directory, "config", "macro-deck", "macro-deck-host.port");
		var legacy = Path.Combine(_directory, "macro-deck-host.port");

		var published = LoopbackPortFiles.Publish(5123, [perUser, legacy]);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(published.Select(file => file.Path), Is.EqualTo(new[] { perUser, legacy }));
			Assert.That(await File.ReadAllTextAsync(perUser), Is.EqualTo("5123"));
			Assert.That(await File.ReadAllTextAsync(legacy), Is.EqualTo("5123"));
		});
	}

	[Test]
	public async Task A_legacy_file_that_cannot_be_written_does_not_cost_the_per_user_file()
	{
		var perUser = Path.Combine(_directory, "per-user.port");
		var legacy = Path.Combine(_directory, "legacy.port");
		Directory.CreateDirectory(legacy);

		var published = LoopbackPortFiles.Publish(5123, [perUser, legacy]);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(published.Select(file => file.Path), Is.EqualTo(new[] { perUser }));
			Assert.That(await File.ReadAllTextAsync(perUser), Is.EqualTo("5123"));
		});
	}

	[Test]
	public async Task A_per_user_file_that_cannot_be_written_does_not_cost_the_legacy_file()
	{
		var blocked = Path.Combine(_directory, "blocked");
		await File.WriteAllTextAsync(blocked, "a file where a directory is needed");
		var legacy = Path.Combine(_directory, "legacy.port");

		var published = LoopbackPortFiles.Publish(5123, [Path.Combine(blocked, "per-user.port"), legacy]);

		Assert.That(published.Select(file => file.Path), Is.EqualTo(new[] { legacy }));
	}

	[Test]
	public void Stopping_removes_the_files_this_host_published()
	{
		var path = Path.Combine(_directory, "host.port");
		var published = LoopbackPortFiles.Publish(5123, [path]);

		LoopbackPortFiles.DeleteOwn(5123, published);

		Assert.That(File.Exists(path), Is.False);
	}

	[Test]
	public async Task Stopping_leaves_the_port_of_a_host_that_took_over()
	{
		var path = Path.Combine(_directory, "host.port");
		var published = LoopbackPortFiles.Publish(5123, [path]);
		await File.WriteAllTextAsync(path, "6000");
		File.SetLastWriteTimeUtc(path, published[0].WrittenAtUtc);

		LoopbackPortFiles.DeleteOwn(5123, published);

		Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("6000"));
	}

	[Test]
	public async Task Stopping_leaves_a_successor_that_got_the_same_port()
	{
		var path = Path.Combine(_directory, "host.port");
		var published = LoopbackPortFiles.Publish(5123, [path]);
		await File.WriteAllTextAsync(path, "5123");
		File.SetLastWriteTimeUtc(path, published[0].WrittenAtUtc.AddSeconds(5));

		LoopbackPortFiles.DeleteOwn(5123, published);

		Assert.That(File.Exists(path), Is.True);
	}
}
