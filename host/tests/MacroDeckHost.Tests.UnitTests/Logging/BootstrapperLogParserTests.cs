using MacroDeckHost.Application.Logging;
using MacroDeckHost.Infrastructure.Logging;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class BootstrapperLogParserTests
{
	[Test]
	public void Parses_A_Bootstrapper_Line_Into_An_Entry()
	{
		var parsed = BootstrapperLogParser.TryParseHeader("[12:34:56 INF] Host answered on port 51234",
			new DateOnly(2026, 7, 28),
			out var draft);

		Assert.That(parsed, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(draft.Source, Is.EqualTo(LogEntrySource.Bootstrapper));
			Assert.That(draft.Level, Is.EqualTo(LogEntryLevel.Information));
			Assert.That(draft.Message, Is.EqualTo("Host answered on port 51234"));
			Assert.That(draft.Timestamp.Year, Is.EqualTo(2026));
			Assert.That(draft.Timestamp.Month, Is.EqualTo(7));
			Assert.That(draft.Timestamp.Day, Is.EqualTo(28));
			Assert.That(draft.Timestamp.Hour, Is.EqualTo(12));
			Assert.That(draft.Timestamp.Minute, Is.EqualTo(34));
			Assert.That(draft.Timestamp.Second, Is.EqualTo(56));
		});
	}

	[TestCase("VRB", LogEntryLevel.Verbose)]
	[TestCase("DBG", LogEntryLevel.Debug)]
	[TestCase("INF", LogEntryLevel.Information)]
	[TestCase("WRN", LogEntryLevel.Warning)]
	[TestCase("ERR", LogEntryLevel.Error)]
	[TestCase("FTL", LogEntryLevel.Fatal)]
	public void Maps_Every_Level_Tag_The_Bootstrapper_Writes(string tag, LogEntryLevel expected)
	{
		Assert.That(BootstrapperLogParser.ParseLevel(tag), Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("no prefix at all")]
	[TestCase("[12:34 INF] time is too short")]
	[TestCase("[12:34:56 INFO] level tag is too long")]
	public void A_Line_That_Is_Not_A_Log_Line_Is_Rejected(string line)
	{
		Assert.That(BootstrapperLogParser.TryParseHeader(line, new DateOnly(2026, 7, 28), out _), Is.False);
	}

	[TestCase("bootstrapper-20260728.log", true)]
	[TestCase("bootstrapper-2026072.log", false)]
	[TestCase("bootstrapper-20261399.log", false)]
	[TestCase("host-20260728.log", false)]
	[TestCase("bootstrapper-20260728.txt", false)]
	public void Only_Daily_Bootstrapper_Files_Carry_A_Date(string fileName, bool expected)
	{
		Assert.That(BootstrapperLogParser.TryParseFileDate(fileName, out _), Is.EqualTo(expected));
	}

	[Test]
	public void The_File_Date_Comes_From_The_File_Name()
	{
		Assert.That(BootstrapperLogParser.TryParseFileDate("bootstrapper-20260728.log", out var date), Is.True);
		Assert.That(date, Is.EqualTo(new DateOnly(2026, 7, 28)));
	}
}
