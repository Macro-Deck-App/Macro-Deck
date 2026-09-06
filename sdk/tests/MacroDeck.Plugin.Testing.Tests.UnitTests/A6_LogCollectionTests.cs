using MacroDeck.Plugin.Protocol.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A6 - log collection preserves structure, filters exclude what does not match, and <c>WaitForAsync</c>
/// genuinely times out rather than hanging.
/// </summary>
[TestFixture]
public class A6_LogCollectionTests
{
	[Test]
	public async Task Log_collection_preserves_structure_and_filters_correctly()
	{
		await using var harness = PluginTestHarness.Create(_ => { });

		var logger = harness.Services.GetRequiredService<ILogger>()
			.ForContext(Constants.SourceContextPropertyName, "A6.Category");

		for (var i = 0; i < 20; i++)
		{
			logger.Information("Routine line {Index}", i);
		}

		logger.Warning("Something needs attention in {Location}", "Berlin");

		var inner = new ArgumentException("bad argument");
		var outer = new InvalidOperationException("boom", inner);
		logger.Error(outer, "A failure occurred");

		var atLeastWarning = harness.Logs.AtLeast(LogLevels.Warning);

		Assert.Multiple(() =>
		{
			Assert.That(atLeastWarning, Has.Count.EqualTo(2));
			Assert.That(atLeastWarning, Has.None.Matches<CollectedLogEvent>(e => e.Level == LogLevels.Information));
		});

		var berlin = harness.Logs.WithProperty("Location", "Berlin");
		var paris = harness.Logs.WithProperty("Location", "Paris");

		Assert.Multiple(() =>
		{
			Assert.That(berlin, Has.Count.EqualTo(1));
			Assert.That(berlin[0].Level, Is.EqualTo(LogLevels.Warning));
			Assert.That(paris, Is.Empty);
		});

		var warningEvent = harness.Logs.Events.First(e => e.Level == LogLevels.Warning);
		var errorEvent = harness.Logs.Events.First(e => e.Level == LogLevels.Error);

		Assert.Multiple(() =>
		{
			Assert.That(warningEvent.SourceContext, Is.EqualTo("A6.Category"));
			// Serilog's own rendering of a string property, quotes included - the same text the wire
			// path (LogEventProjection) hands the host, so a harness assertion and a log-viewer line
			// agree on what a message looks like.
			Assert.That(warningEvent.Message, Is.EqualTo("Something needs attention in \"Berlin\""));
			Assert.That(warningEvent.MessageTemplate, Is.EqualTo("Something needs attention in {Location}"));

			Assert.That(errorEvent.Message, Is.EqualTo("A failure occurred"));
			Assert.That(errorEvent.Exception, Is.Not.Null);
			Assert.That(errorEvent.Exception!.Message, Is.EqualTo("boom"));
			Assert.That(errorEvent.Exception!.Type, Does.Contain(nameof(InvalidOperationException)));
			Assert.That(errorEvent.Exception!.Inner, Is.Not.Null);
			Assert.That(errorEvent.Exception!.Inner!.Message, Is.EqualTo("bad argument"));
			Assert.That(errorEvent.Exception!.Inner!.Type, Does.Contain(nameof(ArgumentException)));
		});
	}

	[Test]
	public async Task WaitForAsync_throws_when_the_text_never_appears()
	{
		await using var harness = PluginTestHarness.Create(_ => { });

		var exception = Assert.ThrowsAsync<PluginTestTimeoutException>(async () => await harness.Logs.WaitForAsync(
			logEvent => logEvent.Message.Contains("never going to appear", StringComparison.Ordinal),
			TimeSpan.FromMilliseconds(150)));

		Assert.That(exception, Is.Not.Null);
	}
}
