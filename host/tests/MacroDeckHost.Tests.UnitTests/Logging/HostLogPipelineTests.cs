using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class HostLogPipelineTests
{
	private const int FileSizeLimit = 4096;

	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.LogsDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void A_Full_File_Rolls_Over_Instead_Of_Dropping_What_Comes_After_It()
	{
		var pipeline = new HostLogPipeline(_paths, new LogLevelState(LogEntryLevel.Information), FileSizeLimit, 1000);

		WriteThroughTwoLoggers(pipeline, 400);

		var files = LogFiles();
		var text = string.Concat(files.Select(File.ReadAllText));
		Assert.Multiple(() =>
		{
			Assert.That(files, Has.Length.GreaterThan(1));
			Assert.That(Enumerable.Range(0, 400)
					.Where(index => !text.Contains($"event {index:0000} "))
					.ToList(),
				Is.Empty);
		});
	}

	[Test]
	public void The_Log_Folder_Stays_Bounded_However_Much_Is_Written()
	{
		const int retainedFiles = 3;
		var pipeline = new HostLogPipeline(_paths,
			new LogLevelState(LogEntryLevel.Information),
			FileSizeLimit,
			retainedFiles);

		WriteThroughTwoLoggers(pipeline, 2000);

		var files = LogFiles();
		var text = string.Concat(files.Select(File.ReadAllText));
		Assert.Multiple(() =>
		{
			// Each of the two writers may hold one file beyond the ones retention kept.
			Assert.That(files, Has.Length.LessThanOrEqualTo(retainedFiles + 2));
			Assert.That(files.Sum(file => new FileInfo(file).Length),
				Is.LessThanOrEqualTo((retainedFiles + 2) * (FileSizeLimit + 512)));
			Assert.That(text, Does.Contain("event 1999 "), "the newest events are the ones kept");
		});
	}

	[Test]
	public void A_Logger_Bound_Before_The_Host_Logger_Exists_Keeps_Sql_Out_Of_The_File()
	{
		var pipeline = new HostLogPipeline(_paths, new LogLevelState(LogEntryLevel.Debug));

		using (var bootstrap = pipeline.Apply(new LoggerConfiguration(), null).CreateLogger())
		using (var factory = new LoggerFactory().AddSerilog(bootstrap))
		{
			var commands = factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
			LoggerMessage.Define(LogLevel.Information, new EventId(1), "Executed DbCommand select-marker")(commands,
				null);
			LoggerMessage.Define(LogLevel.Warning, new EventId(2), "slow-command-marker")(commands, null);
		}

		var text = string.Concat(LogFiles().Select(File.ReadAllText));
		Assert.Multiple(() =>
		{
			Assert.That(text, Does.Not.Contain("select-marker"));
			Assert.That(text, Does.Contain("slow-command-marker"));
		});
	}

	[Test]
	public void Files_Left_Behind_By_A_Version_Without_The_Limit_Are_Removed_And_The_Rest_Kept()
	{
		var oversized = Path.Combine(_paths.LogsDirectory, "host-20260801.log");
		var ordinary = Path.Combine(_paths.LogsDirectory, "host-20260802.log");
		var bootstrapper = Path.Combine(_paths.LogsDirectory, "bootstrapper-20260801.log");
		File.WriteAllBytes(oversized, new byte[3 * 1024 * 1024]);
		File.WriteAllText(ordinary, "kept");
		File.WriteAllBytes(bootstrapper, new byte[3 * 1024 * 1024]);
		var foreign = Path.Combine(_paths.LogsDirectory, "host-old-copy.log");
		File.WriteAllBytes(foreign, new byte[3 * 1024 * 1024]);
		var pipeline = new HostLogPipeline(_paths, new LogLevelState(LogEntryLevel.Information), FileSizeLimit, 3);

		var removed = pipeline.RemoveOversizedFiles();

		Assert.Multiple(() =>
		{
			Assert.That(removed, Is.EqualTo(3 * 1024 * 1024));
			Assert.That(File.Exists(oversized), Is.False);
			Assert.That(File.Exists(ordinary), Is.True);
			Assert.That(File.Exists(bootstrapper), Is.True);
			Assert.That(File.Exists(foreign), Is.True);
		});
	}

	private static void WriteThroughTwoLoggers(HostLogPipeline pipeline, int events)
	{
		using var first = pipeline.Apply(new LoggerConfiguration(), null).CreateLogger();
		using var second = pipeline.Apply(new LoggerConfiguration(), null).CreateLogger();

		for (var index = 0; index < events; index++)
		{
			(index % 2 == 0 ? first : second).Information("event {Index:0000} with some padding to fill the file",
				index);
		}
	}

	private string[] LogFiles()
	{
		return Directory.GetFiles(_paths.LogsDirectory, "host-*.log");
	}
}
