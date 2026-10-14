using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
internal sealed class WidgetDataReaderTests
{
	private const string Owner = "app.macro-deck.twitch";

	private FakeFolderCache _folders = null!;
	private WidgetDataReader _reader = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new FakeFolderCache();
		_reader = new WidgetDataReader(_folders);
	}

	[Test]
	public void A_widget_of_the_callers_own_type_is_read_as_it_is_stored()
	{
		var widget = Add(Owner + "::chat", """{"account":"111","allowModeration":false}""");

		var data = _reader.Read(Owner, widget.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(data?.WidgetId, Is.EqualTo(widget.Id.ToString()));
			Assert.That(data?.WidgetTypeId, Is.EqualTo(Owner + "::chat"));
			Assert.That(data?.Data.GetProperty("account").GetString(), Is.EqualTo("111"));
			Assert.That(data?.Data.GetProperty("allowModeration").GetBoolean(), Is.False);
		});
	}

	[Test]
	public void Another_providers_or_a_built_in_widget_cannot_be_read()
	{
		var foreign = Add("app.macro-deck.youtube::chat", """{"account":"UCmine"}""");
		var builtIn = Add("ActionButton", """{"label":"secret"}""");
		var lookalike = Add(Owner + "-other::chat", "{}");

		Assert.Multiple(() =>
		{
			Assert.That(_reader.Read(Owner, foreign.Id.ToString()), Is.Null);
			Assert.That(_reader.Read(Owner, builtIn.Id.ToString()), Is.Null);
			Assert.That(_reader.Read(Owner, lookalike.Id.ToString()), Is.Null);
			Assert.That(_reader.Read(Owner, Guid.NewGuid().ToString()), Is.Null);
			Assert.That(_reader.Read(Owner, "not-a-guid"), Is.Null);
		});
	}

	[Test]
	public void Stored_data_that_is_not_an_object_reads_as_an_empty_object()
	{
		var widget = Add(Owner + "::chat", "not json");

		Assert.That(_reader.Read(Owner, widget.Id.ToString())?.Data.EnumerateObject(), Is.Empty);
	}

	[Test]
	public async Task An_integration_reads_through_its_own_widget_api_only_what_it_owns()
	{
		var own = Add(Owner + "::stats", "{}");
		var foreign = Add("app.macro-deck.youtube::stats", "{}");
		var api = new IntegrationWidgetApi(Owner, new WidgetApi(null!), new NoIconInvalidator(), _reader);
		var withoutReader = new IntegrationWidgetApi(Owner, new WidgetApi(null!), new NoIconInvalidator());

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await api.GetWidgetDataAsync(own.Id.ToString()), Is.Not.Null);
			Assert.That(await api.GetWidgetDataAsync(foreign.Id.ToString()), Is.Null);
			Assert.That(await withoutReader.GetWidgetDataAsync(own.Id.ToString()), Is.Null);
		});
	}

	private WidgetEntity Add(string type, string data)
	{
		var widget = new WidgetEntity { Id = Guid.NewGuid(), Type = type, Data = data };
		_folders.AddWidget(widget);
		return widget;
	}

	private sealed class NoIconInvalidator : IWidgetIconInvalidator
	{
		public void Invalidate(string ownerId, string actionId)
		{
		}
	}
}
