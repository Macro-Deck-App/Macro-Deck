using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogTailBackgroundServiceTests
{
	private const string Connection = "conn-1";

	private TestPaths _paths = null!;
	private LogLevelState _levelState = null!;
	private LogStreamSubscriptionTracker _subscriptions = null!;
	private RecordingTransport _transport = null!;
	private LogFileReader _reader = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.LogsDirectory);
		_levelState = new LogLevelState(LogEntryLevel.Verbose);
		_subscriptions = new LogStreamSubscriptionTracker();
		_transport = new RecordingTransport();
		_reader = new LogFileReader(_paths, _levelState);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task Nothing_Is_Read_Or_Sent_Without_Subscribers()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("ignored", 1));

		await Tick();

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task An_Entry_Written_Between_The_Page_And_The_Subscribe_Is_Delivered()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("in the page", 1));
		var anchor = _reader.CurrentEnd();

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("written in between", 2),
			TestLogFiles.HostLine("and one after", 3));
		Subscribe(anchor);

		await Tick();

		Assert.That(Delivered(), Does.Contain("written in between"));
	}

	[Test]
	public async Task Entries_The_Page_Already_Carried_Are_Not_Sent_Again()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("in the page", 1),
			TestLogFiles.HostLine("also in the page", 2));
		Subscribe(_reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("fresh", 3));
		await Tick();

		Assert.Multiple(() =>
		{
			Assert.That(Delivered(), Does.Not.Contain("in the page"));
			Assert.That(Delivered(), Does.Contain("fresh"));
		});
	}

	[Test]
	public async Task The_Same_Entry_Is_Never_Delivered_Twice()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		Subscribe(_reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("second", 2),
			TestLogFiles.HostLine("third", 3));
		await Tick();
		await Tick();

		Assert.That(Delivered().Count(message => message == "second"), Is.EqualTo(1));
	}

	[Test]
	public async Task It_Goes_To_The_Subscribers_Own_Group()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		Subscribe(_reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("second", 2));
		await Tick();

		Assert.That(_transport.Sent[0].Group, Is.EqualTo(LogStreamGroups.For(Connection)));
	}

	[Test]
	public async Task Each_Subscriber_Only_Gets_What_Its_Own_Filter_Kept()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		var anchor = _reader.CurrentEnd();
		_subscriptions.Set("errors-only", new LogQuery { Levels = [LogEntryLevel.Error] }, anchor);
		_subscriptions.Set("everything", LogQuery.All, anchor);

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("chatter", 2),
			TestLogFiles.HostLine("broke", 3, "ERR"),
			TestLogFiles.HostLine("after", 4));
		await Tick();

		Assert.Multiple(() =>
		{
			Assert.That(Delivered("errors-only"), Does.Contain("broke"));
			Assert.That(Delivered("errors-only"), Does.Not.Contain("chatter"));
			Assert.That(Delivered("everything"), Does.Contain("chatter"));
		});
	}

	[Test]
	public async Task Unsubscribing_Stops_The_Stream()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		Subscribe(_reader.CurrentEnd());
		_subscriptions.Remove(Connection);

		TestLogFiles.AppendHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("second", 2));
		await Tick();

		Assert.That(_transport.Sent, Is.Empty);
	}

	[Test]
	public async Task A_Roll_Does_Not_Break_The_Stream()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, "20260805", TestLogFiles.HostLine("before", 1, stamp: "20260805"));
		Subscribe(_reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			"20260805",
			TestLogFiles.HostLine("late yesterday", 2, stamp: "20260805"),
			TestLogFiles.HostLine("last of the day", 3, stamp: "20260805"));
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			"20260806",
			TestLogFiles.HostLine("today", 1, stamp: "20260806"),
			TestLogFiles.HostLine("today too", 2, stamp: "20260806"));

		await Tick();
		await Tick();
		await Tick();

		var delivered = Delivered().ToList();
		Assert.Multiple(() =>
		{
			Assert.That(delivered, Does.Contain("late yesterday"));
			Assert.That(delivered, Does.Contain("today"));
			Assert.That(delivered, Is.Unique);
		});
	}

	[Test]
	public async Task A_Stack_Trace_Arrives_Attached_To_Its_Entry()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		Subscribe(_reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("broke", 2, "ERR"),
			TestLogFiles.ExceptionLine("System.InvalidOperationException: boom"),
			TestLogFiles.HostLine("after", 3));
		await Tick();

		var broke = _transport.Sent.SelectMany(sent => sent.Entries).Single(entry => entry.Message == "broke");
		Assert.That(broke.Exception, Is.EqualTo("System.InvalidOperationException: boom"));
	}

	[Test]
	public async Task A_Burst_Larger_Than_One_Read_Does_Not_Repeat_Entries()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		Subscribe(_reader.CurrentEnd());

		var padding = new string('x', 4096);
		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			Enumerable.Range(1, 2000).Select(i => TestLogFiles.HostLine($"{padding} {i}", 2)).ToArray());

		await Tick();
		await Tick();
		await Tick();

		var delivered = Delivered().ToList();
		Assert.That(delivered, Is.Unique);
	}

	// The bootstrapper's file often appears after the host's; its lines must not be skipped forever.
	[Test]
	public async Task A_File_Set_That_Appears_Later_Still_Streams()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("host", 1));
		Subscribe(_reader.CurrentEnd());
		await Tick();

		TestLogFiles.WriteBootstrapper(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.BootstrapperLine("boot-first", 2),
			TestLogFiles.BootstrapperLine("boot-second", 3));
		await Tick();
		await Tick();

		Assert.That(Delivered(), Does.Contain("boot-second"));
	}

	[Test]
	public async Task A_Source_That_Logs_While_Filtered_Out_Still_Reaches_The_Rail()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		_subscriptions.Set(Connection, new LogQuery { Source = LogEntrySource.Host }, _reader.CurrentEnd());

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("host chatter", 2),
			TestLogFiles.HostLine("obs chatter", 3, origin: "Integration/obs"));
		await Tick();

		var sources = _transport.Sent.SelectMany(sent => sent.Sources).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(Delivered(), Does.Not.Contain("obs chatter"), "the integration is filtered out of the list");
			Assert.That(sources.Select(source => source.SourceId),
				Does.Contain("obs"),
				"but it still has to appear in the source rail");
		});
	}

	[Test]
	public async Task A_Range_Whose_End_Is_In_The_Past_Never_Yields_Newly_Appended_Entries()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, TestLogFiles.Stamp, TestLogFiles.HostLine("first", 1));
		var anchor = _reader.CurrentEnd();
		_subscriptions.Set(Connection, new LogQuery { To = TestLogFiles.Timestamp(1) }, anchor);

		TestLogFiles.AppendHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("later", 2),
			TestLogFiles.HostLine("later still", 3));
		await Tick();

		var subscription = _subscriptions.Snapshot().Single(pair => pair.Key == Connection).Value;
		Assert.Multiple(() =>
		{
			Assert.That(Delivered(), Is.Empty, "nothing past the end of the range may be pushed");
			Assert.That(subscription.Position, Is.Not.EqualTo(anchor), "the tail still has to move on");
		});
	}

	private void Subscribe(LogCursor anchor) => _subscriptions.Set(Connection, LogQuery.All, anchor);

	private async Task Tick()
	{
		var service = new LogTailBackgroundService(_reader, _subscriptions, _transport, _levelState);
		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);
	}

	private IEnumerable<string> Delivered(string connectionId = Connection)
		=> _transport.Sent
			.Where(sent => sent.Group == LogStreamGroups.For(connectionId))
			.SelectMany(sent => sent.Entries)
			.Select(entry => entry.Message);

	private sealed class RecordingTransport : IUiTransport
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public List<(string Group, List<LogEntry> Entries, List<LogSourceSummary> Sources)> Sent { get; } = [];

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			if (message is LogEntriesAppendedNotification notification)
			{
				Sent.Add((group, notification.Entries, notification.Sources));
			}

			return Task.CompletedTask;
		}
	}
}
