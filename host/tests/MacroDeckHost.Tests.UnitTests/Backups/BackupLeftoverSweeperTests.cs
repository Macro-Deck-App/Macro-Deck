using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Backups.Storage;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupLeftoverSweeperTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_paths.EnsureDirectoriesExist();
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void A_clean_start_removes_decrypted_staging_and_partial_backups_but_keeps_finished_ones()
	{
		var abandoned = Write(_paths.RestoreStagingDirectory, "1234.failed-20261029T120000Z", "database.db");
		var upload = Write(_paths.RestoreStagingDirectory, "upload" + BackupFileNames.Extension);
		var component = Write(_paths.RestoreStagingDirectory, "component.tmp");
		var creation = Write(_paths.BackupsDirectory, "staging", "op", "database.db");
		var partial = Write(_paths.BackupsDirectory, "nightly" + BackupFileNames.IncompleteExtension);
		var finished = Write(_paths.BackupsDirectory, "nightly" + BackupFileNames.Extension);

		BackupLeftoverSweeper.Sweep(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(abandoned), Is.False);
			Assert.That(File.Exists(upload), Is.False);
			Assert.That(File.Exists(component), Is.False);
			Assert.That(File.Exists(creation), Is.False);
			Assert.That(File.Exists(partial), Is.False);
			Assert.That(File.Exists(finished), Is.True);
		});
	}

	[Test]
	public void A_restore_still_pending_keeps_its_staging()
	{
		var marker = Write(_paths.RestoreStagingDirectory, PendingRestoreDocument.FileName);
		var staged = Write(_paths.RestoreStagingDirectory, "5678", "payload.zip");

		BackupLeftoverSweeper.Sweep(_paths);

		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(marker), Is.True);
			Assert.That(File.Exists(staged), Is.True);
		});
	}

	[Test]
	public void Nothing_is_removed_while_another_process_holds_the_data_directory()
	{
		var partial = Write(_paths.BackupsDirectory, "nightly" + BackupFileNames.IncompleteExtension);
		using var held = new FileStream(Path.Combine(_paths.DataRootDirectory, "restore.lock"),
			FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.None);

		BackupLeftoverSweeper.Sweep(_paths);

		Assert.That(File.Exists(partial), Is.True);
	}

	[Test]
	public void A_cancelled_backup_write_leaves_no_partial_file()
	{
		var provider = new LocalBackupStorageProvider(_paths);
		var request = new BackupStorageWriteRequest("nightly",
			10,
			async (stream, token) =>
			{
				await stream.WriteAsync(new byte[4], token);
				throw new OperationCanceledException(token);
			});

		Assert.ThrowsAsync<OperationCanceledException>(() => provider.Write(request));
		Assert.That(Directory.EnumerateFiles(_paths.BackupsDirectory, "*" + BackupFileNames.IncompleteExtension),
			Is.Empty);
	}

	[Test]
	public void An_aborted_upload_leaves_nothing_in_restore_staging()
	{
		using var cancellation = new CancellationTokenSource();
		using var body = new CancellingStream(cancellation);
		var file = new FormFile(body, 0, 1024, "file", "backup" + BackupFileNames.Extension);

		Assert.ThrowsAsync<OperationCanceledException>(() =>
			BackupHttp.StageUpload(file, _paths, cancellation.Token));
		Assert.That(Directory.EnumerateFiles(_paths.RestoreStagingDirectory), Is.Empty);
	}

	private static string Write(string root, params string[] segments)
	{
		var path = Path.Combine([root, ..segments]);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "x");
		return path;
	}

	private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream(new byte[1024])
	{
		public override async ValueTask<int> ReadAsync(
			Memory<byte> buffer,
			CancellationToken cancellationToken = default)
		{
			await cancellation.CancelAsync();
			cancellationToken.ThrowIfCancellationRequested();
			return 0;
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
		}
	}
}
