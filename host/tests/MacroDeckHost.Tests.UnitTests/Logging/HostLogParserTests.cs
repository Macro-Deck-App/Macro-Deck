using MacroDeckHost.Application.Logging;
using MacroDeckHost.Infrastructure.Logging;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class HostLogParserTests
{
	[Test]
	public void A_Host_Line_Without_A_Category_Parses()
	{
		Assert.That(HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 +02:00 [INF] [Host] Started", out var parsed),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Source, Is.EqualTo(LogEntrySource.Host));
			Assert.That(parsed.SourceId, Is.Null);
			Assert.That(parsed.Category, Is.Null);
			Assert.That(parsed.Level, Is.EqualTo(LogEntryLevel.Information));
			Assert.That(parsed.Message, Is.EqualTo("Started"));
			Assert.That(parsed.Timestamp.Offset, Is.EqualTo(TimeSpan.FromHours(2)));
		});
	}

	[Test]
	public void A_Host_Line_With_A_Category_Parses()
	{
		Assert.That(
			HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 +02:00 [WRN] [Host/UiHub] Slow", out var parsed),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Source, Is.EqualTo(LogEntrySource.Host));
			Assert.That(parsed.Category, Is.EqualTo("UiHub"));
			Assert.That(parsed.Level, Is.EqualTo(LogEntryLevel.Warning));
		});
	}

	[Test]
	public void An_Integration_Line_Carries_Its_Id_And_Category()
	{
		Assert.That(HostLogParser.TryParseHeader(
				"2026-08-06 10:11:12.345 +02:00 [ERR] [Integration/app.macro-deck.spotify/SpotifyClient] Nope",
				out var parsed),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Source, Is.EqualTo(LogEntrySource.Integration));
			Assert.That(parsed.SourceId, Is.EqualTo("app.macro-deck.spotify"));
			Assert.That(parsed.Category, Is.EqualTo("SpotifyClient"));
		});
	}

	[Test]
	public void An_Integration_Line_Without_A_Category_Parses()
	{
		Assert.That(HostLogParser.TryParseHeader(
				"2026-08-06 10:11:12.345 +02:00 [INF] [Integration/app.macro-deck.spotify] Hi",
				out var parsed),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.SourceId, Is.EqualTo("app.macro-deck.spotify"));
			Assert.That(parsed.Category, Is.Null);
		});
	}

	[Test]
	public void A_Line_From_Before_The_Origin_Segment_Still_Parses()
	{
		Assert.That(HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 +02:00 [INF] Old format", out var parsed),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Source, Is.EqualTo(LogEntrySource.Host));
			Assert.That(parsed.SourceId, Is.Null);
			Assert.That(parsed.Category, Is.Null);
			Assert.That(parsed.Message, Is.EqualTo("Old format"));
		});
	}

	[Test]
	public void Brackets_And_Slashes_In_The_Message_Are_Not_Mistaken_For_Fields()
	{
		Assert.That(HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 +02:00 [INF] [Host] a [b] c/d ]",
				out var parsed),
			Is.True);

		Assert.That(parsed.Message, Is.EqualTo("a [b] c/d ]"));
	}

	[Test]
	public void A_Redacted_Line_Still_Parses()
	{
		Assert.That(HostLogParser.TryParseHeader(
				"2026-08-06 10:11:12.345 +02:00 [INF] [Host/UiHub] GET /hubs/ui?access_token=***",
				out var parsed),
			Is.True);

		Assert.That(parsed.Message, Does.Contain("access_token=***"));
	}

	[Test]
	public void A_Negative_Utc_Offset_Parses()
	{
		Assert.That(HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 -05:00 [INF] [Host] Hi", out var parsed),
			Is.True);

		Assert.That(parsed.Timestamp.Offset, Is.EqualTo(TimeSpan.FromHours(-5)));
	}

	[Test]
	public void An_Unknown_Level_Tag_Falls_Back_To_Information()
	{
		Assert.That(HostLogParser.TryParseHeader("2026-08-06 10:11:12.345 +02:00 [XYZ] [Host] Hi", out var parsed),
			Is.True);

		Assert.That(parsed.Level, Is.EqualTo(LogEntryLevel.Information));
	}

	// The writer indents every line of an exception block, so text it did not compose - an exception
	// message built from an API response, say - cannot pose as an entry of its own.
	[Test]
	public void An_Indented_Line_That_Would_Otherwise_Look_Like_A_Header_Is_Not_One()
	{
		Assert.That(HostLogParser.TryParseHeader(" 2026-08-06 10:11:12.345 +02:00 [ERR] [Host] forged", out _),
			Is.False);
	}

	[Test]
	public void A_Stack_Frame_Is_Not_A_Header()
	{
		Assert.That(HostLogParser.TryParseHeader("   at MacroDeckHost.Ui.UiHub.SubscribeLogs()", out _), Is.False);
	}

	[Test]
	public void Only_Daily_Host_Files_Are_Recognised()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostLogParser.TryParseFileStamp("host-20260806.log", out var stamp), Is.True);
			Assert.That(stamp, Is.EqualTo("20260806"));
			Assert.That(HostLogParser.TryParseFileStamp("bootstrapper-20260806.log", out _), Is.False);
			Assert.That(HostLogParser.TryParseFileStamp("host-notadate.log", out _), Is.False);
		});
	}
}
