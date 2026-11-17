using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventBusNonFiniteTests
{
	private const string EventId = "com.example::reading";

	private EventSubscriptionIndex _index = null!;
	private EventSampleStore _samples = null!;
	private EventBus _bus = null!;
	private IntegrationEventPublisher _publisher = null!;

	private static IEnumerable<TestCaseData> NonFiniteValues()
	{
		yield return new TestCaseData(double.NaN).SetName("NaN");
		yield return new TestCaseData(double.PositiveInfinity).SetName("PositiveInfinity");
		yield return new TestCaseData(double.NegativeInfinity).SetName("NegativeInfinity");
		yield return new TestCaseData(float.NaN).SetName("SingleNaN");
		yield return new TestCaseData(float.NegativeInfinity).SetName("SingleNegativeInfinity");
		yield return new TestCaseData(new List<object?> { 1, double.NaN }).SetName("NaNInsideAList");
		yield return new TestCaseData(new Dictionary<string, object?> { ["inner"] = double.PositiveInfinity })
			.SetName("InfinityInsideANestedObject");
	}

	[SetUp]
	public void SetUp()
	{
		_index = new EventSubscriptionIndex(new StubFolderCache(), new StubAutomationCache());
		_samples = new EventSampleStore();
		_bus = new EventBus(_index, _samples, Log.Logger);
		_publisher = new IntegrationEventPublisher("com.example", _bus, Log.Logger);
		var data = """
				   {"flows":"[{\"triggerId\":\"t1\",\"triggerType\":\"onEvent\",\"event\":{\"providerId\":\"com.example\",\"eventId\":\"reading\"},\"children\":[]}]"}
				   """;
		_index.ReindexWidget(Guid.NewGuid(), data);
	}

	[TestCaseSource(nameof(NonFiniteValues))]
	public void A_built_in_occurrence_with_a_non_finite_number_reaches_neither_triggers_nor_samples(object value)
	{
		Assert.DoesNotThrow(() => _publisher.Publish("reading",
			new Dictionary<string, object?> { ["label"] = "ok", ["value"] = value }));

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Reader.TryRead(out _), Is.False);
			Assert.That(_samples.TryGetLast(EventId), Is.Null);
		});
	}

	[Test]
	public void An_occurrence_from_any_producer_is_held_to_the_same_rule()
	{
		_bus.Publish(new EventOccurrence(EventId,
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = double.NaN }));

		Assert.That(_bus.Reader.TryRead(out _), Is.False);
	}

	[Test]
	public void A_self_referencing_collection_does_not_hang_the_check()
	{
		var items = new List<object?> { 1.5 };
		items.Add(items);
		items.Add(items);

		_publisher.Publish("reading", new Dictionary<string, object?> { ["value"] = items });

		Assert.That(_bus.Reader.TryRead(out _), Is.True);
	}

	[Test]
	public void Finite_edge_values_are_still_published_unchanged()
	{
		var parameters = new Dictionary<string, object?>
		{
			["max"] = double.MaxValue,
			["min"] = double.MinValue,
			["epsilon"] = double.Epsilon,
			["single"] = float.MaxValue,
			["decimal"] = decimal.MaxValue,
			["long"] = long.MaxValue,
			["text"] = "NaN",
			["nested"] = new List<object?> { 1.5, null }
		};

		_publisher.Publish("reading", parameters);

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Reader.TryRead(out var occurrence), Is.True);
			Assert.That(occurrence?.Parameters, Is.EqualTo(parameters));
		});
	}
}
