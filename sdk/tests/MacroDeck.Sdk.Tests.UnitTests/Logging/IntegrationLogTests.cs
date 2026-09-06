using MacroDeck.Sdk.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Sdk.Tests.UnitTests.Logging;

/// <summary>
/// An integration logs through its own logger so sinks can attribute the entry. The id has to be on
/// the event itself - deriving it from a namespace would break for integrations shipped separately.
/// </summary>
public class IntegrationLogTests
{
	private CollectingSink _sink = null!;
	private ILogger _previousLogger = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new CollectingSink();
		_previousLogger = Log.Logger;
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(_sink)
			.CreateLogger();
	}

	[TearDown]
	public void TearDown()
	{
		Log.Logger = _previousLogger;
	}

	[Test]
	public void An_Integration_Logger_Stamps_The_Integration_Id()
	{
		IntegrationLog.For("app.macro-deck.spotify").Information("Connected");

		Assert.That(ReadProperty(_sink.Events.Single(), IntegrationLog.IntegrationPropertyName),
			Is.EqualTo("app.macro-deck.spotify"));
	}

	[Test]
	public void A_Typed_Integration_Logger_Also_Carries_The_Source_Context()
	{
		IntegrationLog.For<IntegrationLogTests>("app.macro-deck.obs").Warning("Reconnecting");

		var logEvent = _sink.Events.Single();

		Assert.Multiple(() =>
		{
			Assert.That(ReadProperty(logEvent, IntegrationLog.IntegrationPropertyName),
				Is.EqualTo("app.macro-deck.obs"));
			Assert.That(ReadProperty(logEvent, "SourceContext"),
				Is.EqualTo(typeof(IntegrationLogTests).FullName));
		});
	}

	[Test]
	public void A_Runtime_Source_Type_Is_Supported_For_Static_Helpers()
	{
		// A runtime Type, which is the case this overload exists for (a static helper or a base
		// class logging on behalf of its implementations).
		var source = typeof(IntegrationLogTests);

		IntegrationLog.For("app.macro-deck.obs", source).Debug("Wrote variable");

		Assert.That(ReadProperty(_sink.Events.Single(), "SourceContext"),
			Is.EqualTo(typeof(IntegrationLogTests).FullName));
	}

	private static string? ReadProperty(LogEvent logEvent, string name)
		=> logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text }
			? text
			: null;

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
