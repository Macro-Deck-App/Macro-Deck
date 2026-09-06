using System.Globalization;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Envelope;

[TestFixture]
public class ProtocolEnvelopeSerializationTests
{
	private const string SampleId = "0f8fad5b-d9cb-469f-a165-70867728950e";

	[Test]
	public void Field_names_are_camel_case_on_the_wire()
	{
		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionHello,
			Id = SampleId,
			CorrelationId = "correlation-id",
			SentAt = DateTimeOffset.UtcNow,
			ProtocolVersion = 1,
			DeadlineMs = 1000,
			IdempotencyKey = "key",
		};

		var json = ProtocolEnvelopeWriter.WriteToString(envelope);
		using var document = JsonDocument.Parse(json);

		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.TryGetProperty("type", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("id", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("correlationId", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("sentAt", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("protocolVersion", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("deadlineMs", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("idempotencyKey", out _), Is.True);
		});
	}

	[Test]
	public void Null_optional_members_are_omitted_from_the_wire()
	{
		var envelope = new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = SampleId };

		var json = ProtocolEnvelopeWriter.WriteToString(envelope);
		using var document = JsonDocument.Parse(json);

		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.TryGetProperty("correlationId", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("sentAt", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("protocolVersion", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("deadlineMs", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("idempotencyKey", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("payload", out _), Is.False);
			Assert.That(document.RootElement.TryGetProperty("error", out _), Is.False);
		});
	}

	[Test]
	public void An_envelope_with_an_unknown_optional_member_round_trips_without_error()
	{
		var json = $$"""
					 {
					 	"type": "session.ping",
					 	"id": "{{SampleId}}",
					 	"futureField": { "nested": true },
					 	"anotherUnknownMember": 42
					 }
					 """;

		ProtocolEnvelope? envelope = null;
		Assert.DoesNotThrow(() =>
			envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json, PluginProtocolJson.Options));

		Assert.That(envelope, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(envelope!.Type, Is.EqualTo(MessageTypes.SessionPing));
			Assert.That(envelope.Id, Is.EqualTo(SampleId));
		});

		// Unknown members are dropped, not echoed back onto the wire on the next write.
		var roundTripped = ProtocolEnvelopeWriter.WriteToString(envelope!);
		Assert.That(roundTripped, Does.Not.Contain("futureField"));
	}

	[Test]
	public void Round_trip_preserves_every_field_compared_by_value_and_payload_by_raw_text()
	{
		using var payloadDocument = JsonDocument.Parse("""{"volume":42,"muted":false}""");
		var original = new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = SampleId,
			CorrelationId = "corr-1",
			SentAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z", CultureInfo.InvariantCulture),
			ProtocolVersion = 1,
			DeadlineMs = 5000,
			IdempotencyKey = "idem-1",
			Payload = payloadDocument.RootElement.Clone(),
		};

		var json = ProtocolEnvelopeWriter.WriteToString(original);
		var actual = JsonSerializer.Deserialize<ProtocolEnvelope>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);

		// JsonElement does not override Equals, so comparing the envelopes directly with
		// Is.EqualTo would fall back to reference comparison and fail. Compare field-by-field and
		// compare the payload by its raw JSON text instead.
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Type, Is.EqualTo(original.Type));
			Assert.That(actual.Id, Is.EqualTo(original.Id));
			Assert.That(actual.CorrelationId, Is.EqualTo(original.CorrelationId));
			Assert.That(actual.SentAt, Is.EqualTo(original.SentAt));
			Assert.That(actual.ProtocolVersion, Is.EqualTo(original.ProtocolVersion));
			Assert.That(actual.DeadlineMs, Is.EqualTo(original.DeadlineMs));
			Assert.That(actual.IdempotencyKey, Is.EqualTo(original.IdempotencyKey));
			Assert.That(actual.Payload.HasValue, Is.True);
			Assert.That(actual.Payload!.Value.GetRawText(), Is.EqualTo(original.Payload!.Value.GetRawText()));
		});
	}

	[Test]
	public void Payload_and_error_each_round_trip_independently()
	{
		var error = new ProtocolError
		{
			Code = ProtocolErrorCodes.InternalError,
			Message = ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError),
			Retryable = false,
		};
		var envelope = new ProtocolEnvelope { Type = MessageTypes.ProtocolError, Id = SampleId, Error = error };

		var json = ProtocolEnvelopeWriter.WriteToString(envelope);
		var actual = JsonSerializer.Deserialize<ProtocolEnvelope>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Payload, Is.Null);
			Assert.That(actual.Error, Is.Not.Null);
			Assert.That(actual.Error!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
		});
	}

	[Test]
	public void A_payload_nested_past_max_json_depth_fails_as_malformed_envelope_rather_than_throwing()
	{
		var input = BuildDeeplyNestedPayload(ProtocolLimits.MaxJsonDepth + 16);

		ProtocolReadResult result = default!;
		Assert.DoesNotThrow(() => result = ProtocolEnvelopeReader.Read(input));

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.Not.Null);
			Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.MalformedEnvelope));
		});
	}

	private static byte[] BuildDeeplyNestedPayload(int depth)
	{
		var builder = new StringBuilder();
		builder.Append($$"""{"type":"session.ping","id":"{{SampleId}}","payload":""");
		for (var i = 0; i < depth; i++)
		{
			builder.Append("""{"n":""");
		}

		builder.Append('0');
		for (var i = 0; i < depth; i++)
		{
			builder.Append('}');
		}

		builder.Append('}');
		return Encoding.UTF8.GetBytes(builder.ToString());
	}
}
