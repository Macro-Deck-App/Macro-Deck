using System.Globalization;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
[NonParallelizable]
public class RedactedSinkFanOutTests
{
	private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzY29wZSI6ImFkbWluIn0.dBjftJeZ4CVP-mB92K27uhbUJU1p";

	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Test]
	public void The_File_And_The_Console_Carry_The_Same_Sanitized_Message()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(home))
		{
			Assert.Ignore("The operating system reports no home directory.");
		}

		var logPath = Path.Combine(_directory, "host-.log");
		var databasePath = Path.Combine(home, "MacroDeck", "database.db");
		var console = new StringWriter(CultureInfo.InvariantCulture);
		var originalOut = Console.Out;

		Console.SetOut(console);
		try
		{
			using (var logger = new LoggerConfiguration()
				.MinimumLevel.Verbose()
				.Enrich.With(new LogOriginEnricher())
				.WriteTo.Redacted(sinks =>
				{
					sinks.Console(formatProvider: CultureInfo.InvariantCulture);
					sinks.File(RedactingTextFormatter.ForFileSink(), logPath);
				})
				.CreateLogger())
			{
				logger.Error(new InvalidOperationException("could not read /home/other-user/token.json"),
					"Opening {Path} with ?access_token={Token}",
					databasePath,
					Jwt);
			}
		}
		finally
		{
			Console.SetOut(originalOut);
		}

		var file = File.ReadAllText(Path.Combine(_directory, Directory.GetFiles(_directory).Single()));
		var consoleText = console.ToString();
		var header = file.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];
		Assert.That(HostLogParser.TryParseHeader(header, out var parsed), Is.True, $"could not parse: {header}");

		Assert.Multiple(() =>
		{
			Assert.That(file, Is.Not.Empty, "the entry should have reached the file sink");

			foreach (var output in new[] { file, consoleText })
			{
				Assert.That(output, Does.Not.Contain(home));
				Assert.That(output, Does.Not.Contain(Jwt));
				Assert.That(output, Does.Not.Contain("other-user"));
				Assert.That(output, Does.Contain("MacroDeck"));
				Assert.That(output, Does.Contain("/home/<user>/token.json"));
			}

			// Not merely "both are clean": both must be clean in the same way. Quotes are ignored
			// because the console sink renders string properties quoted - a difference in the output
			// template, not in what was redacted.
			Assert.That(Unquote(consoleText), Does.Contain(Unquote(parsed.Message)));
		});
	}

	private static string Unquote(string text) => text.Replace("\"", string.Empty, StringComparison.Ordinal);
}
