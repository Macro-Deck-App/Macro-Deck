using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventPreviewSamplesTests
{
	private const string EventId = "music-player::track-changed";

	private VariableTemplateRenderer _renderer = null!;
	private EventSampleStore _samples = null!;
	private EventPreviewSamples _preview = null!;

	[SetUp]
	public void SetUp()
	{
		_renderer = new VariableTemplateRenderer(new VariableRegistry());
		_samples = new EventSampleStore();
		_preview = new EventPreviewSamples(new StubEventRegistry(), _samples);
	}

	[Test]
	public void A_declared_parameter_previews_empty_rather_than_as_the_literal_reference()
	{
		Assert.That(Render("{{ event.trackName }}"), Is.Empty);
	}

	[Test]
	public void The_last_occurrence_beats_the_declared_default()
	{
		Record(("trackName", "Real"));

		Assert.That(Render("{{ event.trackName }}"), Is.EqualTo("Real"));
	}

	[Test]
	public void Supplied_values_beat_the_last_occurrence()
	{
		Record(("trackName", "Real"));

		Assert.That(Render("{{ event.trackName }}", ("trackName", "Made up")), Is.EqualTo("Made up"));
	}

	[Test]
	public void A_parameter_the_caller_left_out_keeps_the_recorded_value()
	{
		Record(("trackName", "Real"), ("volume", 0.5d));

		Assert.That(Render("{{ event.volume }}", ("trackName", "Made up")), Is.EqualTo("0.5"));
	}

	[Test]
	public void An_unknown_event_leaves_the_context_untouched()
	{
		var context = _renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();

		Assert.That(_preview.Overlay(context, "nope::nope"), Is.SameAs(context));
	}

	private void Record(params (string Name, object? Value)[] parameters)
		=> _samples.Record(new EventOccurrence(EventId,
			parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)));

	private string Render(string template, params (string Name, object? Value)[] overrides)
	{
		var context = _renderer.CreateContextAsync(VariableScope.Global, null).GetAwaiter().GetResult();
		var overlaid = _preview.Overlay(context,
			EventId,
			overrides.Length == 0
				? null
				: overrides.ToDictionary(o => o.Name, o => o.Value, StringComparer.Ordinal));
		return _renderer.Render(template, overlaid);
	}

	private sealed class StubEventRegistry : IEventRegistry
	{
		private static readonly EventDefinitionDescriptor _trackChanged = new(QualifiedId.Parse(EventId),
			"music-player",
			"Music Player",
			false,
			new EventDefinition
			{
				Id = "track-changed",
				Name = "Track Changed",
				PayloadParameters =
				[
					ActionParameter.Text("trackName", label: "Track"),
					ActionParameter.Number("volume", label: "Volume")
				]
			});

		public IReadOnlyList<EventDefinitionDescriptor> GetDefinitions() => [_trackChanged];

		public EventDefinitionDescriptor? Find(string qualifiedEventId)
			=> qualifiedEventId == EventId ? _trackChanged : null;

		public object? FindProvider(string qualifiedEventId) => null;
	}
}
