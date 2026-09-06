using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogFileReaderTests
{
	private const string Yesterday = "20260805";
	private const string Today = "20260806";

	private TestPaths _paths = null!;
	private LogLevelState _levelState = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.LogsDirectory);
		_levelState = new LogLevelState(LogEntryLevel.Verbose);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void A_Multi_Line_Exception_Folds_Into_The_Entry_Above_It()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("Broke", 1, "ERR"),
			TestLogFiles.ExceptionLine("System.InvalidOperationException: boom"),
			TestLogFiles.ExceptionLine("   at MacroDeckHost.X.Y()"),
			TestLogFiles.HostLine("Recovered", 2));

		var page = Read();

		Assert.Multiple(() =>
		{
			Assert.That(page.Entries, Has.Count.EqualTo(2));
			Assert.That(page.Entries[0].Message, Is.EqualTo("Broke"));
			Assert.That(page.Entries[0].Exception,
				Is.EqualTo("System.InvalidOperationException: boom\n   at MacroDeckHost.X.Y()"));
			Assert.That(page.Entries[1].Exception, Is.Null);
		});
	}

	[Test]
	public void Both_File_Sets_Merge_Chronologically()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("host-first", 1),
			TestLogFiles.HostLine("host-last", 3));
		TestLogFiles.WriteBootstrapper(_paths.LogsDirectory, Today, TestLogFiles.BootstrapperLine("boot", 2));

		var page = Read();

		string[] expected = ["host-first", "boot", "host-last"];
		Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[Test]
	public void Paging_Continues_Into_The_Previous_Day()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Yesterday,
			TestLogFiles.HostLine("old", 1, stamp: Yesterday));
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("new", 1));

		var newest = Read(limit: 1);
		var older = Read(limit: 1, before: newest.Older);

		Assert.Multiple(() =>
		{
			Assert.That(newest.Entries.Single().Message, Is.EqualTo("new"));
			Assert.That(older.Entries.Single().Message, Is.EqualTo("old"));
			Assert.That(older.Older, Is.Null, "the oldest retained file has been reached");
		});
	}

	[Test]
	public void A_Cursor_Into_A_Pruned_File_Does_Not_Replay_Newer_Entries()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Yesterday,
			TestLogFiles.HostLine("old", 1, stamp: Yesterday));
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("new", 1));

		var newest = Read(limit: 1);
		File.Delete(Path.Combine(_paths.LogsDirectory, $"host-{Yesterday}.log"));

		var older = Read(limit: 10, before: newest.Older);

		Assert.That(older.Entries, Is.Empty, "the pruned entries are gone, and the newer ones must not repeat");
	}

	// A day can roll between two page requests; the newer file must not leak into an older page.
	[Test]
	public void A_File_Created_After_The_First_Page_Does_Not_Duplicate_Into_The_Older_One()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Yesterday,
			TestLogFiles.HostLine("first", 1, stamp: Yesterday),
			TestLogFiles.HostLine("second", 2, stamp: Yesterday));

		var newest = Read(limit: 1);
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("brand-new", 1));

		var older = Read(limit: 10, before: newest.Older);

		Assert.That(older.Entries.Select(entry => entry.Message), Has.No.Member("brand-new"));
	}

	[Test]
	public void The_Configured_Minimum_Filters_Bootstrapper_Entries_Only()
	{
		_levelState.Minimum = LogEntryLevel.Warning;
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("host-info", 1));
		TestLogFiles.WriteBootstrapper(_paths.LogsDirectory,
			Today,
			TestLogFiles.BootstrapperLine("boot-info", 2),
			TestLogFiles.BootstrapperLine("boot-warn", 3, "WRN"));

		var page = Read();

		string[] expected = ["host-info", "boot-warn"];
		Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[TestCase("spotify", 1)]
	[TestCase("obs", 0)]
	public void An_Integration_Filter_Is_Applied_While_Reading(string integrationId, int expected)
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("from spotify", 1, origin: "Integration/spotify"));

		var page = Read(query: new LogQuery { IntegrationId = integrationId });

		Assert.That(page.Entries, Has.Count.EqualTo(expected));
	}

	[Test]
	public void A_Search_Covers_The_Message_And_The_Exception()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("plain", 1),
			TestLogFiles.HostLine("wrapped", 2, "ERR"),
			TestLogFiles.ExceptionLine("System.TimeoutException: needle"));

		Assert.Multiple(() =>
		{
			Assert.That(Read(query: new LogQuery { Search = "NEEDLE" }).Entries.Single().Message,
				Is.EqualTo("wrapped"));
			Assert.That(Read(query: new LogQuery { Search = "plain" }).Entries.Single().Message, Is.EqualTo("plain"));
		});
	}

	[Test]
	public void A_Search_That_Matches_Nothing_Terminates()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			Enumerable.Range(1, 50).Select(i => TestLogFiles.HostLine($"line {i}", i)).ToArray());

		var page = Read(query: new LogQuery { Search = "nothing matches this" });

		Assert.Multiple(() =>
		{
			Assert.That(page.Entries, Is.Empty);
			Assert.That(page.Older, Is.Null, "the whole retained history was scanned");
		});
	}

	[Test]
	public void The_Source_Rail_Lists_Every_Stream_That_Logged_Once_Each()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("h1", 1),
			TestLogFiles.HostLine("h2", 2),
			TestLogFiles.HostLine("i1", 3, origin: "Integration/obs"),
			TestLogFiles.HostLine("i2", 4, origin: "Integration/obs"));
		TestLogFiles.WriteBootstrapper(_paths.LogsDirectory,
			Today,
			TestLogFiles.BootstrapperLine("b1", 5),
			TestLogFiles.BootstrapperLine("b2", 6));

		var sources = Reader().ReadSources();

		(LogEntrySource Source, string? SourceId)[] expected =
		[
			(LogEntrySource.Host, null),
			(LogEntrySource.Bootstrapper, null),
			(LogEntrySource.Integration, "obs")
		];
		Assert.That(sources.Select(source => (source.Source, source.SourceId)), Is.EquivalentTo(expected));
	}

	[Test]
	public void A_Time_Range_Keeps_Both_Of_Its_Boundary_Entries_And_Nothing_Outside()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("s1", 1),
			TestLogFiles.HostLine("s5", 5),
			TestLogFiles.HostLine("s10", 10),
			TestLogFiles.HostLine("s15", 15));

		var page = Read(query: new LogQuery { From = At(5), To = At(10) });

		string[] expected = ["s5", "s10"];
		Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	[Test]
	public void A_Range_That_Matches_Nothing_Ends_The_Paging_Instead_Of_Looping_It()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("before", 1),
			TestLogFiles.HostLine("after", 10));

		var page = Read(query: new LogQuery { From = At(4), To = At(5) });

		Assert.Multiple(() =>
		{
			Assert.That(page.Entries, Is.Empty);
			Assert.That(page.Older, Is.Null, "an empty page that still reports more would loop the viewer");
			Assert.That(LogCursor.TryParse(page.TailAnchor.Encode(), out _), Is.True);
		});
	}

	[Test]
	public void Paging_Older_Inside_A_Range_Stops_At_The_Range_Start()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Yesterday,
			TestLogFiles.HostLine("y1", 1, stamp: Yesterday),
			TestLogFiles.HostLine("y2", 2, stamp: Yesterday),
			TestLogFiles.HostLine("y3", 3, stamp: Yesterday));
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("t1", 1),
			TestLogFiles.HostLine("t2", 2),
			TestLogFiles.HostLine("t3", 3));

		var query = new LogQuery { From = At(1) };
		var page = Read(query: query, limit: 2);
		var collected = page.Entries.Select(entry => entry.Message).ToList();
		var followUps = 0;

		while (page.Older is { } older && followUps < 5)
		{
			page = Read(query: query, limit: 2, before: older);
			collected.InsertRange(0, page.Entries.Select(entry => entry.Message));
			followUps++;
		}

		string[] expected = ["t1", "t2", "t3"];
		Assert.Multiple(() =>
		{
			Assert.That(collected, Is.EqualTo(expected));
			Assert.That(page.Older, Is.Null);
			Assert.That(followUps, Is.LessThanOrEqualTo(2));
		});
	}

	[Test]
	public void A_Search_Inside_A_Stack_Trace_Returns_The_One_Entry_It_Belongs_To()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("Broke", 1, "ERR"),
			TestLogFiles.ExceptionLine("System.InvalidOperationException: boom"),
			TestLogFiles.ExceptionLine("   at MacroDeckHost.Needle.Y()"),
			TestLogFiles.HostLine("Recovered", 2));

		var page = Read(query: new LogQuery { Search = "needle" });

		Assert.That(page.Entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(page.Entries[0].Message, Is.EqualTo("Broke"));
			Assert.That(page.Entries[0].Exception,
				Is.EqualTo("System.InvalidOperationException: boom\n   at MacroDeckHost.Needle.Y()"));
		});
	}

	[Test]
	public void A_Limit_Counts_Entries_Not_The_Lines_Of_Their_Stack_Traces()
	{
		var trace = Enumerable.Range(1, 200)
			.Select(i => TestLogFiles.ExceptionLine($"   at MacroDeckHost.Frame{i}()"));
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			[
				TestLogFiles.HostLine("A", 1, "ERR"),
				.. trace,
				TestLogFiles.HostLine("B", 2),
				TestLogFiles.HostLine("C", 3)
			]);

		var newest = Read(limit: 2);
		var older = Read(limit: 2, before: newest.Older);

		string[] expectedNewest = ["B", "C"];
		Assert.That(newest.Entries.Select(entry => entry.Message), Is.EqualTo(expectedNewest));
		Assert.Multiple(() =>
		{
			Assert.That(older.Entries.Single().Message, Is.EqualTo("A"));
			Assert.That(older.Entries.Single().Exception,
				Does.Contain("Frame1()").And.Contain("Frame200()"));
		});
	}

	[Test]
	public void An_Entry_Id_Round_Trips_To_The_Position_It_Was_Read_From()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("only", 1));

		var entry = Read().Entries.Single();

		Assert.That(LogEntryId.TryParse(entry.Id, out var kind, out var position), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(kind, Is.EqualTo(LogFileKind.Host));
			Assert.That(position.FileStamp, Is.EqualTo(Today));
			Assert.That(position.Offset, Is.EqualTo(0));
		});
	}

	// The rail is the only way to filter to an integration, and it reaches a bounded window back from
	// the newest entry - so what falls inside that window, and what drops off it, is part of the contract.
	[Test]
	public void The_Source_Rail_Reaches_A_Week_Back_And_No_Further()
	{
		const string WithinTheWindow = "20260803";
		const string BeyondTheWindow = "20260727";

		TestLogFiles.WriteHost(_paths.LogsDirectory,
			BeyondTheWindow,
			TestLogFiles.HostLine("ancient", 1, origin: "Integration/ancient", stamp: BeyondTheWindow));
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			WithinTheWindow,
			TestLogFiles.HostLine("recent", 1, origin: "Integration/recent", stamp: WithinTheWindow));
		TestLogFiles.WriteHost(_paths.LogsDirectory, Today, TestLogFiles.HostLine("now", 1));

		var sources = Reader().ReadSources();

		(LogEntrySource Source, string? SourceId)[] expected =
		[
			(LogEntrySource.Host, null),
			(LogEntrySource.Integration, "recent")
		];
		Assert.That(sources.Select(source => (source.Source, source.SourceId)), Is.EquivalentTo(expected));
	}

	// A narrow range over a busy day file - the host writes tens of thousands of lines a day - must
	// reach its matches in one page: entries outside the range are skipped against their own budget
	// instead of consuming the match-scan budget.
	[Test]
	public void A_Range_Below_A_Long_Run_Of_Newer_Entries_Still_Returns_Its_Matches_In_One_Page()
	{
		var newer = Enumerable.Range(0, LogFileReader.MaxScannedEntries + 1)
			.Select(i => TestLogFiles.HostLine($"newer {i}", 10 + i));
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			[
				TestLogFiles.HostLine("wanted-first", 1),
				TestLogFiles.HostLine("wanted-last", 2),
				.. newer
			]);

		var page = Read(query: new LogQuery { From = At(1), To = At(2) });

		string[] expected = ["wanted-first", "wanted-last"];
		Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	// Day files that lie entirely below the requested range are never opened: the scan stops at the
	// floor, so a range over today does not walk - and get cut short by - a week of retained history.
	[Test]
	public void A_Range_Does_Not_Walk_The_Day_Files_Below_It()
	{
		var belowFloor = new[] { "20260801", "20260802", "20260803" };
		var perFile = (LogFileReader.MaxSkippedEntries / belowFloor.Length) + 100;
		foreach (var stamp in belowFloor)
		{
			TestLogFiles.WriteHost(_paths.LogsDirectory,
				stamp,
				Enumerable.Range(0, perFile).Select(i => TestLogFiles.HostLine($"ancient {i}", i % 60, stamp: stamp))
					.ToArray());
		}

		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("wanted", 1),
			TestLogFiles.HostLine("also-wanted", 2));

		var page = Read(query: new LogQuery { From = At(1) });

		string[] expected = ["wanted", "also-wanted"];
		Assert.Multiple(() =>
		{
			Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
			Assert.That(page.Older, Is.Null, "the scan reached the range floor, so there is nothing older to page to");
		});
	}

	// The files are read while other threads append to them, so their timestamps are not monotonic.
	// One reordered line below the range must not end the scan for that query.
	[Test]
	public void An_Out_Of_Order_Entry_Below_The_Range_Does_Not_End_The_Scan()
	{
		TestLogFiles.WriteHost(_paths.LogsDirectory,
			Today,
			TestLogFiles.HostLine("older-in-range", 10),
			TestLogFiles.HostLine("reordered-below-range", 1),
			TestLogFiles.HostLine("newer-in-range", 20));

		var page = Read(query: new LogQuery { From = At(5) });

		string[] expected = ["older-in-range", "newer-in-range"];
		Assert.That(page.Entries.Select(entry => entry.Message), Is.EqualTo(expected));
	}

	private static DateTimeOffset At(int second, string stamp = Today) => TestLogFiles.Timestamp(second, stamp);

	private LogFileReader Reader() => new(_paths, _levelState);

	private LogPage Read(LogQuery? query = null, int limit = 100, LogCursor? before = null)
		=> Reader().ReadPage(query ?? LogQuery.All, before, limit);
}
