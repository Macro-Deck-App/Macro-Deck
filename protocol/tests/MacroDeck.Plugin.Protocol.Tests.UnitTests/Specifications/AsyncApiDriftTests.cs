using System.Reflection;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Specifications;

/// <summary>
/// Asserts asyncapi.yaml and the C# contracts agree, in both directions. Together with
/// <see cref="OpenApiDriftTests" /> this is what stops a hand-authored specification drifting from the
/// contracts it claims to describe.
/// </summary>
[TestFixture]
public class AsyncApiDriftTests
{
	[Test]
	public void Document_is_asyncapi_3_0()
	{
		Assert.That(SpecificationDocuments.AsyncApi["asyncapi"], Is.EqualTo("3.0.0"));
		Assert.That(SpecificationDocuments.AsyncApi.ContainsKey("channels"), Is.True);
	}

	[Test]
	public void Every_message_type_is_documented_and_the_reverse()
	{
		Assert.That(DocumentedMessageTypes(), Is.EquivalentTo(MessageTypes.All));
	}

	[Test]
	public void Each_documented_message_carries_the_declared_direction()
	{
		var messages = SpecificationDocuments.Map(SpecificationDocuments.AsyncApi, "components", "messages");

		Assert.Multiple(() =>
		{
			foreach (var message in messages.Values.Select(SpecificationDocuments.AsMap))
			{
				var type = (string)message["x-macrodeck-message-type"]!;
				var documented = (string)message["x-macrodeck-direction"]!;

				Assert.That(MessageTypeDirections.TryGetDirection(type, out var direction),
					Is.True,
					$"no declared direction for '{type}'");

				var declared = direction.ToString();
				Assert.That(documented,
					Is.EqualTo(char.ToLowerInvariant(declared[0]) + declared[1..]),
					$"direction for '{type}'");
			}
		});
	}

	[Test]
	public void Close_codes_match_the_contracts()
	{
		var documented = SpecificationDocuments.AsMap(SpecificationDocuments.AsyncApi["x-macrodeck-close-codes"]);

		var declared = typeof(ProtocolCloseCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(int))
			.ToDictionary(field => char.ToLowerInvariant(field.Name[0]) + field.Name[1..],
				field => (long)(int)field.GetValue(null)!,
				StringComparer.Ordinal);

		Assert.That(documented.Keys, Is.EquivalentTo(declared.Keys));
		Assert.Multiple(() =>
		{
			foreach (var (name, value) in declared)
			{
				Assert.That(SpecificationDocuments.AsLong(documented[name]), Is.EqualTo(value), $"close code '{name}'");
			}
		});
	}

	[Test]
	public void The_backpressure_exempt_set_matches_the_contracts()
	{
		var documented = SpecificationDocuments.AsStrings(
			SpecificationDocuments.AsyncApi["x-macrodeck-backpressure-exempt-types"]);

		var declared = MessageTypes.All.Where(ProtocolBackpressure.IsExemptWhilePaused);

		Assert.That(documented, Is.EquivalentTo(declared));
	}

	[Test]
	public void Error_codes_and_protocol_versions_match_the_contracts()
	{
		var versions = SpecificationDocuments.AsMap(SpecificationDocuments.AsyncApi["x-macrodeck-protocol-versions"]);

		Assert.Multiple(() =>
		{
			Assert.That(SpecificationDocuments.AsStrings(SpecificationDocuments.AsyncApi["x-macrodeck-error-codes"]),
				Is.EquivalentTo(ProtocolErrorCodes.All));
			Assert.That(SpecificationDocuments.AsLong(versions["minimum"]), Is.EqualTo(ProtocolVersions.Minimum));
			Assert.That(SpecificationDocuments.AsLong(versions["current"]), Is.EqualTo(ProtocolVersions.Current));
		});
	}

	[Test]
	public void The_envelope_schema_matches_the_envelope_record()
	{
		var schema = SpecificationDocuments.Map(SpecificationDocuments.AsyncApi,
			"components",
			"schemas",
			"ProtocolEnvelope");

		var declared = typeof(ProtocolEnvelope)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
			.ToList();

		var required = typeof(ProtocolEnvelope)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttributes()
				.Any(attribute => attribute.GetType().Name == "RequiredMemberAttribute"))
			.Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(SpecificationDocuments.Map(schema, "properties").Keys, Is.EquivalentTo(declared));
			Assert.That(SpecificationDocuments.AsStrings(schema["required"]), Is.EquivalentTo(required));
		});
	}

	private static IReadOnlyList<string> DocumentedMessageTypes()
		=>
		[
			.. SpecificationDocuments.Map(SpecificationDocuments.AsyncApi, "components", "messages")
				.Values
				.Select(SpecificationDocuments.AsMap)
				.Select(message => (string)message["x-macrodeck-message-type"]!)
		];
}
