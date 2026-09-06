using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Envelope;

/// <summary>
/// The opaque-payload write path skips input validation, which is only safe while every element it can
/// be handed came from a strict parse. These tests pin both halves of that rule: the parser settings the
/// converter depends on, and the framework behaviour that makes relaxing them unsafe.
/// </summary>
[TestFixture]
public class RawJsonElementConverterTests
{
	[Test]
	public void The_protocol_parser_stays_strict_enough_for_the_unvalidated_write_path()
		=> Assert.Multiple(() =>
		{
			Assert.That(PluginProtocolJson.Options.ReadCommentHandling,
				Is.EqualTo(JsonCommentHandling.Disallow),
				"A skipped comment survives in an element's raw text and would be written into the envelope.");
			Assert.That(PluginProtocolJson.Options.AllowTrailingCommas,
				Is.False,
				"A tolerated trailing comma survives in an element's raw text and would be written into the envelope.");
		});

	[Test]
	public void A_leniently_parsed_element_keeps_the_text_a_strict_reader_would_reject()
	{
		var lenient = new JsonDocumentOptions
		{
			CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
		};

		using var document = JsonDocument.Parse("{\"a\":1, /* note */ \"b\":[2,],}", lenient);
		var raw = document.RootElement.GetRawText();

		// Not a claim about what the protocol does - a claim about why it may not relax its parser.
		Assert.Multiple(() =>
		{
			Assert.That(raw, Does.Contain("/* note */"));
			Assert.That(raw, Does.Contain("[2,]"));
		});
	}

	[Test]
	public void An_opaque_member_written_through_the_protocol_serializer_stays_valid_json()
	{
		using var document = JsonDocument.Parse("{\"z\":1,\"a\":\"caf\\u00e9 a<b\"}");

		var envelope = new ProtocolEnvelope
		{
			Type = "host.invoke", Id = "1", Payload = document.RootElement.Clone()
		};

		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope);

		Assert.DoesNotThrow(() =>
			{
				using var reparsed = JsonDocument.Parse(bytes);
			},
			"The unvalidated raw write emitted something a strict reader cannot parse.");

		Assert.That(Encoding.UTF8.GetString(bytes),
			Does.Contain("{\"z\":1,\"a\":\"caf\\u00e9 a<b\"}"),
			"The opaque member was re-encoded instead of relayed.");
	}
}
