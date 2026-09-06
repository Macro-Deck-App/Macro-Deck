using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class GetLogsRequestMessageHandlerTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.LogsDirectory);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task It_Returns_The_Newest_Entries_Oldest_First()
	{
		WriteHost("First", "Second", "Third");

		var response = await Handle(new GetLogsRequest());

		string[] expected = ["First", "Second", "Third"];
		Assert.That(response.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[Test]
	public async Task A_Limit_Keeps_The_Most_Recent_Entries()
	{
		WriteHost("First", "Second", "Third");

		var response = await Handle(new GetLogsRequest { Limit = 2 });

		string[] expected = ["Second", "Third"];
		Assert.That(response.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[Test]
	public async Task The_Cursor_Reads_The_Page_Before_Without_Gaps_Or_Repeats()
	{
		WriteHost("First", "Second", "Third", "Fourth");

		var newest = await Handle(new GetLogsRequest { Limit = 2 });
		var older = await Handle(new GetLogsRequest { Limit = 2, Before = newest.OlderCursor });

		string[] expectedNewest = ["Third", "Fourth"];
		string[] expectedOlder = ["First", "Second"];
		Assert.Multiple(() =>
		{
			Assert.That(newest.Entries.Select(entry => entry.Message), Is.EqualTo(expectedNewest));
			Assert.That(older.Entries.Select(entry => entry.Message), Is.EqualTo(expectedOlder));
		});
	}

	[Test]
	public async Task Reaching_The_Oldest_Entry_Reports_No_More()
	{
		WriteHost("Only");

		var response = await Handle(new GetLogsRequest());

		Assert.Multiple(() =>
		{
			Assert.That(response.HasMore, Is.False);
			Assert.That(response.OlderCursor, Is.Null);
		});
	}

	[Test]
	public async Task An_Unparseable_Cursor_Restarts_At_The_Newest_Entries()
	{
		WriteHost("First", "Second");

		var response = await Handle(new GetLogsRequest { Before = "not-a-cursor" });

		Assert.That(response.Entries, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task A_Level_Filter_Is_Applied_While_Reading()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			TestLogFiles.HostLine("Chatter", 1),
			TestLogFiles.HostLine("Broke", 2, "ERR"));

		var response = await Handle(new GetLogsRequest { Levels = [LogEntryLevel.Error] });

		string[] expected = ["Broke"];
		Assert.That(response.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[Test]
	public async Task The_Tail_Anchor_Points_Past_The_Newest_Entry_The_Page_Returned()
	{
		WriteHost("First", "Second");

		var response = await Handle(new GetLogsRequest());

		Assert.That(LogCursor.TryParse(response.TailAnchor, out var anchor), Is.True);
		Assert.That(anchor.Host.Offset, Is.GreaterThan(0), "the tail must resume after what the page held");
	}

	[Test]
	public async Task An_Empty_Logs_Directory_Is_Not_An_Error()
	{
		var response = await Handle(new GetLogsRequest());

		Assert.Multiple(() =>
		{
			Assert.That(response.Entries, Is.Empty);
			Assert.That(response.HasMore, Is.False);
		});
	}

	[Test]
	public async Task A_Time_Range_On_The_Request_Reaches_The_Reader()
	{
		WriteHost("First", "Second", "Third", "Fourth");

		var response = await Handle(new GetLogsRequest
		{
			From = TestLogFiles.Timestamp(2),
			To = TestLogFiles.Timestamp(3)
		});

		string[] expected = ["Second", "Third"];
		Assert.That(response.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	private void WriteHost(params string[] messages)
		=> TestLogFiles.WriteHost(_paths.LogsDirectory,
			TestLogFiles.Stamp,
			messages.Select((message, index) => TestLogFiles.HostLine(message, index + 1)).ToArray());

	private async Task<GetLogsResponse> Handle(GetLogsRequest request)
	{
		var reader = new LogFileReader(_paths, new LogLevelState(LogEntryLevel.Verbose));

		return await new GetLogsRequestMessageHandler(reader).Handle(request, CancellationToken.None);
	}
}
