using MacroDeckHost.Infrastructure.Variables;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableFileSystemTests
{
	private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

	private string _folder = null!;
	private VariableFileSystem _files = null!;

	[SetUp]
	public void SetUp()
	{
		_folder = Path.Combine(Path.GetTempPath(), "macro-deck-watch-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_folder);
		_files = new VariableFileSystem(Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_files.Dispose();
		if (Directory.Exists(_folder))
		{
			Directory.Delete(_folder, true);
		}
	}

	[Test]
	public async Task An_external_write_to_a_watched_file_is_reported()
	{
		var path = Path.Combine(_folder, "value.txt");
		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var watch = _files.Watch(path, () => changed.TrySetResult());

		await File.WriteAllTextAsync(path, "hello");

		Assert.That(await Task.WhenAny(changed.Task, Task.Delay(EventTimeout)), Is.SameAs(changed.Task));
		Assert.That((await _files.ReadAsync(path)).Content, Is.EqualTo("hello"));
	}

	[Test]
	public async Task A_file_in_a_folder_that_does_not_exist_yet_is_reported_once_the_folder_appears()
	{
		var folder = Path.Combine(_folder, "later");
		var path = Path.Combine(folder, "value.txt");
		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var watch = _files.Watch(path, () => changed.TrySetResult());

		Directory.CreateDirectory(folder);
		await File.WriteAllTextAsync(path, "late");

		Assert.That(await Task.WhenAny(changed.Task, Task.Delay(EventTimeout)), Is.SameAs(changed.Task));
	}

	[Test]
	public async Task A_missing_file_a_folder_or_a_file_over_the_limit_reads_as_unavailable()
	{
		var tooLarge = Path.Combine(_folder, "large.txt");
		await File.WriteAllTextAsync(tooLarge, new string('x', 300 * 1024));

		Assert.Multiple(async () =>
		{
			Assert.That((await _files.ReadAsync(Path.Combine(_folder, "missing.txt"))).Available, Is.False);
			Assert.That((await _files.ReadAsync(_folder)).Available, Is.False);
			Assert.That((await _files.ReadAsync(tooLarge)).Available, Is.False);
		});
	}

	[Test]
	public async Task Writing_keeps_the_file_itself_and_writes_no_byte_order_mark()
	{
		var target = Path.Combine(_folder, "target.txt");
		await File.WriteAllTextAsync(target, "old");
		var link = Path.Combine(_folder, "link.txt");
		if (!OperatingSystem.IsWindows())
		{
			File.CreateSymbolicLink(link, target);
		}

		await _files.WriteAsync(OperatingSystem.IsWindows() ? target : link, "new");

		Assert.Multiple(async () =>
		{
			Assert.That(await File.ReadAllBytesAsync(target), Is.EqualTo("new"u8.ToArray()));
			if (!OperatingSystem.IsWindows())
			{
				Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(target));
			}
		});
	}
}
