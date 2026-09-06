using System.Text.Json;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Negotiation;

/// <summary>The acceptance criterion: negotiation exists day one and never fails fatally. Literal
/// expected versions throughout - never <c>UiModelVersions.Current</c> - so a test reading the same
/// constant under test cannot pass alongside a wrong constant.</summary>
[TestFixture]
public class UiCapabilityNegotiatorTests
{
	private static UiCapabilities Capabilities(int minimum,
		int maximum,
		bool supportsAll = false,
		IReadOnlyDictionary<string, UiVersionRange>? components = null)
		=> new()
		{
			UiProtocol = new UiVersionRange { Minimum = minimum, Maximum = maximum },
			SupportsAllComponents = supportsAll,
			Components = components ?? new Dictionary<string, UiVersionRange>(),
		};

	[Test]
	public void Model_negotiation_accepts_a_renderer_at_the_current_version()
	{
		var result = UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(3, 3));

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(3));
			Assert.That(result.FallbackReason, Is.Null);
		});
	}

	[Test]
	public void Model_negotiation_clamps_a_future_renderer_down_to_ours()
	{
		var result = UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(UiModelVersions.Minimum,
			UiModelVersions.Current + 3));

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(UiModelVersions.Current));
		});
	}

	[TestCase(1, 2)]
	[TestCase(0, 0)]
	[TestCase(-3, -1)]
	public void Model_negotiation_degrades_below_the_floor_without_throwing(int minimum, int maximum)
	{
		UiNegotiationResult? result = null;
		Assert.DoesNotThrow(() =>
			result = UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(minimum, maximum)));

		Assert.Multiple(() =>
		{
			Assert.That(result!.IsSupported, Is.False);
			Assert.That(result.NegotiatedVersion, Is.Null);
			Assert.That(result.FallbackReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Model_negotiation_rejects_a_renderer_whose_minimum_exceeds_ours()
	{
		var result = UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(UiModelVersions.Current + 1,
			UiModelVersions.Current + 2));

		Assert.That(result.IsSupported, Is.False);
	}

	[Test]
	public void Component_negotiation_supports_everything_when_the_renderer_says_so()
	{
		var node = new UiNode { Id = "n", Type = "vendor.chart", RequiredComponentVersion = 9 };
		var capabilities = Capabilities(1, 1, supportsAll: true);

		var result = UiCapabilityNegotiator.NegotiateComponent(node, capabilities);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(9));
		});
	}

	[Test]
	public void A_node_without_a_required_version_negotiates_at_version_one()
	{
		var node = new UiNode { Id = "n", Type = "button" };
		var capabilities = Capabilities(1,
			1,
			components: new Dictionary<string, UiVersionRange> { ["button"] = new() { Minimum = 1, Maximum = 3 } });

		var result = UiCapabilityNegotiator.NegotiateComponent(node, capabilities);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.NegotiatedVersion, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_component_below_the_renderers_minimum_is_unsupported()
	{
		var node = new UiNode { Id = "n", Type = "button", RequiredComponentVersion = 1 };
		var capabilities = Capabilities(1,
			1,
			components: new Dictionary<string, UiVersionRange> { ["button"] = new() { Minimum = 2, Maximum = 4 } });

		var result = UiCapabilityNegotiator.NegotiateComponent(node, capabilities);

		Assert.That(result.IsSupported, Is.False);
	}

	[Test]
	public void A_component_above_the_renderers_maximum_is_unsupported()
	{
		var node = new UiNode { Id = "n", Type = "chart", RequiredComponentVersion = 5 };
		var capabilities = Capabilities(1,
			1,
			components: new Dictionary<string, UiVersionRange> { ["chart"] = new() { Minimum = 1, Maximum = 3 } });

		var result = UiCapabilityNegotiator.NegotiateComponent(node, capabilities);

		Assert.That(result.IsSupported, Is.False);
	}

	[Test]
	public void An_unlisted_component_degrades_rather_than_throwing()
	{
		var node = new UiNode { Id = "n", Type = "mystery" };
		var capabilities = Capabilities(1, 1, supportsAll: false);

		UiNegotiationResult? result = null;
		Assert.DoesNotThrow(() => result = UiCapabilityNegotiator.NegotiateComponent(node, capabilities));

		Assert.Multiple(() =>
		{
			Assert.That(result!.IsSupported, Is.False);
			Assert.That(result.FallbackReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Malformed_capabilities_degrade_rather_than_throwing()
	{
		var invertedRangeNode = new UiNode { Id = "n", Type = "chart", RequiredComponentVersion = 3 };
		var invertedCapabilities = Capabilities(1,
			1,
			components: new Dictionary<string, UiVersionRange> { ["chart"] = new() { Minimum = 4, Maximum = 2 } });

		var emptyTypeNode = new UiNode { Id = "n", Type = "" };
		var emptyTypeCapabilities = Capabilities(1, 1);

		UiNegotiationResult? invertedResult = null;
		UiNegotiationResult? emptyTypeResult = null;
		Assert.DoesNotThrow(() =>
		{
			invertedResult = UiCapabilityNegotiator.NegotiateComponent(invertedRangeNode, invertedCapabilities);
			emptyTypeResult = UiCapabilityNegotiator.NegotiateComponent(emptyTypeNode, emptyTypeCapabilities);
		});

		Assert.Multiple(() =>
		{
			Assert.That(invertedResult!.IsSupported, Is.False);
			Assert.That(emptyTypeResult!.IsSupported, Is.False);
		});
	}

	[Test]
	public void A_component_entry_that_arrives_as_json_null_degrades_rather_than_throwing()
	{
		const string json =
			"""{"uiProtocol":{"minimum":1,"maximum":1},"supportsAllComponents":false,"components":{"button":null}}""";

		var capabilities = JsonSerializer.Deserialize<UiCapabilities>(json, UiCanonicalJson.Options)!;

		UiNegotiationResult? result = null;
		Assert.DoesNotThrow(() =>
			result = UiCapabilityNegotiator.NegotiateComponent(new UiNode { Id = "n", Type = "button" }, capabilities));

		Assert.Multiple(() =>
		{
			Assert.That(result!.IsSupported, Is.False);
			Assert.That(result.FallbackReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Negotiation_results_never_carry_a_version_and_a_reason_together()
	{
		var node = new UiNode { Id = "n", Type = "chart", RequiredComponentVersion = 1 };
		var results = new[]
		{
			UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(1, 1)),
			UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(1, 7)),
			UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(0, 0)),
			UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(-3, -1)),
			UiCapabilityNegotiator.NegotiateModelVersion(Capabilities(2, 5)),
			UiCapabilityNegotiator.NegotiateComponent(node, Capabilities(1, 1, supportsAll: true)),
			UiCapabilityNegotiator.NegotiateComponent(node,
				Capabilities(1,
					1,
					components: new Dictionary<string, UiVersionRange>
					{
						["chart"] = new() { Minimum = 1, Maximum = 3 },
					})),
			UiCapabilityNegotiator.NegotiateComponent(node, Capabilities(1, 1)),
		};

		Assert.Multiple(() =>
		{
			foreach (var result in results)
			{
				Assert.That(result.NegotiatedVersion is null, Is.EqualTo(!result.IsSupported));
				Assert.That(result.FallbackReason is null, Is.EqualTo(result.IsSupported));
			}
		});
	}
}
