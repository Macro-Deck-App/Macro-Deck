using System.Globalization;
using MacroDeckHost.Logging;
using MacroDeck.Sdk.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogOriginEnricherTests
{
	[Test]
	public void An_Event_Without_Context_Is_The_Host()
	{
		Assert.That(Origin(), Is.EqualTo("Host"));
	}

	[Test]
	public void A_Source_Context_Becomes_Its_Short_Type_Name()
	{
		Assert.That(Origin(sourceContext: "MacroDeckHost.Ui.UiHub"), Is.EqualTo("Host/UiHub"));
	}

	[Test]
	public void An_Integration_Is_Named_By_Its_Id()
	{
		Assert.That(Origin(integrationId: "app.macro-deck.spotify"), Is.EqualTo("Integration/app.macro-deck.spotify"));
	}

	[Test]
	public void An_Integration_Keeps_Its_Category_Too()
	{
		Assert.That(Origin("MacroDeckSpotify.SpotifyClient", "app.macro-deck.spotify"),
			Is.EqualTo("Integration/app.macro-deck.spotify/SpotifyClient"));
	}

	[TestCase("has space", ExpectedResult = "Integration/has_space")]
	[TestCase("auth=1", ExpectedResult = "Integration/auth_1")]
	[TestCase("key:value", ExpectedResult = "Integration/key_value")]
	[TestCase("br[ack]ets", ExpectedResult = "Integration/br_ack_ets")]
	[TestCase("sla/sh", ExpectedResult = "Integration/sla_sh")]
	public string An_Unruly_Integration_Id_Is_Sanitized(string integrationId) => Origin(integrationId: integrationId);

	private static string Origin(string? sourceContext = null, string? integrationId = null)
	{
		var properties = new List<LogEventProperty>();
		if (sourceContext is not null)
		{
			properties.Add(new LogEventProperty("SourceContext", new ScalarValue(sourceContext)));
		}

		if (integrationId is not null)
		{
			properties.Add(new LogEventProperty(IntegrationLog.IntegrationPropertyName,
				new ScalarValue(integrationId)));
		}

		var logEvent = new LogEvent(DateTimeOffset.UnixEpoch,
			LogEventLevel.Information,
			null,
			new MessageTemplate([]),
			properties);

		new LogOriginEnricher().Enrich(logEvent, new PropertyFactory());

		return logEvent.Properties[LogOriginEnricher.OriginPropertyName] is ScalarValue { Value: string origin }
			? origin
			: string.Empty;
	}

	private sealed class PropertyFactory : ILogEventPropertyFactory
	{
		public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
			=> new(name, new ScalarValue(Convert.ToString(value, CultureInfo.InvariantCulture)));
	}
}
