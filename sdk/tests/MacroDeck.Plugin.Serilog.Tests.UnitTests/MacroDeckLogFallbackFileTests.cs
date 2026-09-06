using MacroDeck.Plugin.Protocol.Logging;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

/// <summary>
/// <see cref="MacroDeckLogFallbackFile" /> against a real temporary directory - the filesystem is a
/// genuine external boundary here, not something to fake.
/// </summary>
[TestFixture]
public class MacroDeckLogFallbackFileTests
{
	[Test]
	public void The_local_fallback_file_stays_bounded_when_the_transport_is_unavailable()
	{
		var directory = Path.Combine(Path.GetTempPath(), "macrodeck-fallback-tests", Guid.NewGuid().ToString("N"));
		var path = Path.Combine(directory, "plugin-fallback.log");
		const int maxBytes = 4096;

		try
		{
			var fallback = new MacroDeckLogFallbackFile(path, maxBytes);

			const string firstMessage = "FIRST-DISTINCTIVE-MESSAGE";
			const string newestMessage = "NEWEST-DISTINCTIVE-MESSAGE";

			fallback.Append([BuildDto(DateTimeOffset.UtcNow, firstMessage)]);

			// Write far more than the cap.
			for (var i = 0; i < 500; i++)
			{
				fallback.Append([
					BuildDto(DateTimeOffset.UtcNow.AddMilliseconds(i),
						$"padding message number {i} to fill up the fallback file")
				]);
			}

			fallback.Append([BuildDto(DateTimeOffset.UtcNow, newestMessage)]);

			var bytes = new FileInfo(path).Length;
			var content = File.ReadAllText(path);

			Assert.Multiple(() =>
			{
				// A little slack for the one line that pushed it over the cap, not unbounded growth.
				Assert.That(bytes, Is.LessThanOrEqualTo(maxBytes + 512));
				Assert.That(content, Does.Contain(newestMessage));
				Assert.That(content, Does.Not.Contain(firstMessage));
			});
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	[Test]
	public void Reset_clears_a_previously_written_fallback_file()
	{
		var directory = Path.Combine(Path.GetTempPath(), "macrodeck-fallback-tests", Guid.NewGuid().ToString("N"));
		var path = Path.Combine(directory, "plugin-fallback.log");

		try
		{
			var fallback = new MacroDeckLogFallbackFile(path, 4096);
			fallback.Append([BuildDto(DateTimeOffset.UtcNow, "stale entry from a past outage")]);

			Assume.That(File.Exists(path), Is.True);

			fallback.Reset();

			Assert.That(File.ReadAllText(path), Is.Empty);
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	private static LogEventDto BuildDto(DateTimeOffset timestamp, string message) => new()
	{
		Timestamp = timestamp,
		Level = LogLevels.Information,
		MessageTemplate = message,
		RenderedMessage = message
	};
}
