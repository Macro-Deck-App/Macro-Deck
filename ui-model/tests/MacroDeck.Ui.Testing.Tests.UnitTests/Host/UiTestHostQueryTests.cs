using MacroDeck.Ui.Config;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

/// <summary>
/// Regression coverage for the two query properties every other test in the suite silently depends on: a query
/// for something absent says so instead of handing back a null that fails somewhere else, and a query for
/// several things returns them in the order the tree renders them. Each test traces to acceptance scenario 37
/// or 38 for issue #540.
/// </summary>
[TestFixture]
public class UiTestHostQueryTests
{
	private static readonly string[] _expectedStepIds = ["setup.credentials", "setup.verify"];

	private static readonly string[] _expectedObjectIds = ["endpoint", "headers.h1", "headers.h2"];

	/// <summary>The one node whose text carries "API" - the apiKey input's label.</summary>
	private static readonly string[] _expectedApiTextIds = ["apiKey"];

	/// <summary>The types whose match order is compared against the walk: one that occurs twice under different
	/// parents, one that mixes an authored container with repeated items, and one that occurs at several depths.
	/// </summary>
	private static readonly string[] _orderedTypes =
		[UiConfigPrimitives.Step, UiConfigPrimitives.Object, UiConfigPrimitives.String];

	[Test]
	public void Querying_a_missing_node_fails_loudly_and_probing_for_one_does_not()
	{
		var host = SampleFlowHost.Render();

		Assert.Multiple(() =>
		{
			Assert.That(() => host.ById("nope"),
				Throws.TypeOf<UiTestAssertionException>().And.Message.Contains("nope"));
			Assert.That(host.FindById("nope"), Is.Null);

			// Two steps match, so there is no single one - the failure a test would otherwise discover as a wrong
			// node rather than as an ambiguous query.
			Assert.That(() => host.SingleByType(UiConfigPrimitives.Step),
				Throws.TypeOf<UiTestAssertionException>().And.Message.Contains(UiConfigPrimitives.Step));
			Assert.That(() => host.SingleByType("nope"), Throws.TypeOf<UiTestAssertionException>());
			Assert.That(host.SingleByType(UiConfigPrimitives.Flow).Id, Is.EqualTo(host.Root.Id));
			Assert.That(host.SingleByType(UiConfigPrimitives.Flow).Id, Is.EqualTo("setup"));
		});
	}

	[Test]
	public void ByType_returns_matches_in_document_order()
	{
		var host = SampleFlowHost.Render();

		// The walk is the definition of document order, written here rather than borrowed from the host: a shared
		// walk would let both agree on the same wrong order.
		var walked = new List<UiTestNode>();
		Walk(host.Root, walked);

		Assert.Multiple(() =>
		{
			Assert.That(host.ByType(UiConfigPrimitives.Step).Select(node => node.Id),
				Is.EqualTo(_expectedStepIds).AsCollection);
			Assert.That(host.ByType(UiConfigPrimitives.Object).Select(node => node.Id),
				Is.EqualTo(_expectedObjectIds).AsCollection);

			foreach (var type in _orderedTypes)
			{
				Assert.That(host.ByType(type).Select(node => node.Id),
					Is.EqualTo(walked
							.Where(node => string.Equals(node.Type, type, StringComparison.Ordinal))
							.Select(node => node.Id))
						.AsCollection,
					$"the '{type}' matches have to be in document order");
			}
		});
	}

	[Test]
	public void ByText_matches_an_ordinal_substring_of_a_text_bearing_property()
	{
		var host = SampleFlowHost.Render();

		Assert.Multiple(() =>
		{
			Assert.That(host.ByText("API").Select(node => node.Id),
				Is.EqualTo(_expectedApiTextIds).AsCollection,
				"the apiKey input's label is the only text carrying it");

			// Ordinal, not culture-aware or case-insensitive: a match that depended on the machine's locale would
			// make every text assertion in the suite machine-dependent.
			Assert.That(host.ByText("api"), Is.Empty);
			Assert.That(host.ByText("nothing carries this"), Is.Empty);
		});
	}

	private static void Walk(UiTestNode node, List<UiTestNode> into)
	{
		into.Add(node);

		foreach (var child in node.Children)
		{
			Walk(child, into);
		}

		if (node.Fallback is { } fallback)
		{
			Walk(fallback, into);
		}
	}
}
