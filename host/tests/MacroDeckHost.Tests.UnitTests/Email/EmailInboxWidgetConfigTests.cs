using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.SystemIntegrations.Email;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.EmailInbox;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxWidgetConfigTests
{
	private static readonly string[] _listOnly =
	[
		EmailWidgetTypes.UnreadOnlyKey, EmailWidgetTypes.MaxMessagesKey, EmailWidgetTypes.ShowSenderKey,
		EmailWidgetTypes.ShowSubjectKey, EmailWidgetTypes.ShowPreviewKey, EmailWidgetTypes.ShowTimestampKey
	];

	private EmailInboxWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new EmailInboxWidgetHarness();
		await _harness.SyncAsync();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task The_configuration_starts_from_the_registered_defaults()
	{
		var registry = TestWidgetTypeProviders.Registry();
		await new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None)
			.StartAsync(new EmailIntegration());
		Assert.That(registry.TryResolve(EmailWidgetTypes.QualifiedId, out var inbox), Is.True);

		var host = await RenderAsync(JsonDocument.Parse(inbox.Descriptor.DefaultData!).RootElement);

		Assert.Multiple(() =>
		{
			Assert.That(host.ById(EmailWidgetTypes.ModeKey).Text(UiConfigProperties.Value),
				Is.EqualTo(EmailWidgetTypes.ModeCount));
			Assert.That(host.ById(EmailWidgetTypes.ModeKey).Property(UiConfigProperties.Options)!.Value
					.EnumerateArray()
					.Select(option => option.GetProperty("value").GetString()),
				Is.EqualTo(new[] { EmailWidgetTypes.ModeCount, EmailWidgetTypes.ModeList }));
			Assert.That(
				host.ById(EmailWidgetTypes.InboxesKey).Property(UiConfigProperties.Value)!.Value.GetArrayLength(),
				Is.Zero,
				"no selection means every inbox");
			Assert.That(host.ById(EmailWidgetTypes.MaxMessagesKey).Number(UiConfigProperties.Value), Is.EqualTo(5));
			Assert.That(host.ById(EmailWidgetTypes.MaxMessagesKey).Number(UiConfigProperties.Min), Is.EqualTo(1));
			Assert.That(host.ById(EmailWidgetTypes.MaxMessagesKey).Number(UiConfigProperties.Max), Is.EqualTo(10));
			Assert.That(Flag(host, EmailWidgetTypes.UnreadOnlyKey), Is.False);
			Assert.That(Flag(host, EmailWidgetTypes.ShowSenderKey), Is.True);
			Assert.That(Flag(host, EmailWidgetTypes.ShowSubjectKey), Is.True);
			Assert.That(Flag(host, EmailWidgetTypes.ShowPreviewKey), Is.False);
			Assert.That(Flag(host, EmailWidgetTypes.ShowTimestampKey), Is.True);
		});
	}

	[Test]
	public async Task The_list_settings_show_only_for_the_message_list()
	{
		var host = await RenderAsync(Data(new { }));
		var asCount = Visibility(host);

		host.ById(EmailWidgetTypes.ModeKey).Change(EmailWidgetTypes.ModeList);
		await host.SettleAsync();
		var asList = Visibility(host);

		Assert.Multiple(() =>
		{
			Assert.That(asCount.Values, Is.All.False);
			Assert.That(asList.Values, Is.All.True);
		});
	}

	[Test]
	public async Task Every_connected_inbox_is_offered_with_its_service_and_a_stored_selection_is_kept()
	{
		var host = await RenderAsync(Data(new { inboxes = new[] { EmailInboxWidgetHarness.HomeRef } }));
		var inboxes = host.ById(EmailWidgetTypes.InboxesKey);
		var options = inboxes.Property(UiConfigProperties.Options)!.Value.EnumerateArray().ToList();
		var work = options.Single(option => option.GetProperty("value").GetString() == EmailInboxWidgetHarness.WorkRef);

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.GetProperty("value").GetString()),
				Is.EquivalentTo(new[] { EmailInboxWidgetHarness.WorkRef, EmailInboxWidgetHarness.HomeRef }));
			Assert.That(Resolve(work.GetProperty("label").Deserialize<LocalizedText>()),
				Is.EqualTo(Resolve(Localization.AppStrings.Events.Email.InboxOption("Work",
					"IMAP"))));
			Assert.That(inboxes.Property(UiConfigProperties.Value)!.Value.EnumerateArray()
					.Select(item => item.GetString()),
				Is.EqualTo(new[] { EmailInboxWidgetHarness.HomeRef }));
		});
	}

	[Test]
	public async Task Edited_configurations_validate_against_the_registered_widget_schema()
	{
		var registry = TestWidgetTypeProviders.Registry();
		await new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None)
			.StartAsync(new EmailIntegration());
		var schemas = new WidgetDataSchemaProvider(registry);

		var host = await RenderAsync(Data(new { }));
		host.ById(EmailWidgetTypes.InboxesKey).Change(new[] { EmailInboxWidgetHarness.WorkRef });
		host.ById(EmailWidgetTypes.ModeKey).Change(EmailWidgetTypes.ModeList);
		host.ById(EmailWidgetTypes.MaxMessagesKey).Change(8);
		host.ById(EmailWidgetTypes.UnreadOnlyKey).Change(true);
		host.ById(EmailWidgetTypes.ShowPreviewKey).Change(true);
		await host.SettleAsync();
		var edited = Compose(host, """[{"triggerType":"onShortPress","children":[]}]""");

		Assert.Multiple(() =>
		{
			Assert.That(schemas.TryGet(EmailWidgetTypes.QualifiedId, out var schema), Is.True);
			Assert.That(WidgetDataSchema.Validate(schema!, edited), Is.Empty);
			Assert.That(edited.GetProperty(EmailWidgetTypes.MaxMessagesKey).GetDouble(), Is.EqualTo(8));
			Assert.That(WidgetDataSchema.Validate(schema!, Data(new { mode = "grid" })),
				Is.Not.Empty,
				"an unknown mode is refused");
			Assert.That(WidgetDataSchema.Validate(schema!, Data(new { maxMessages = 11 })),
				Is.Not.Empty,
				"the list shows at most ten emails");
		});
	}

	private async Task<UiTestHost> RenderAsync(JsonElement data)
	{
		var host = UiTestHost.Render(EmailInboxWidgetConfigViews.Build(data, _harness.World.Cache));
		await host.SettleAsync();
		return host;
	}

	private static bool? Flag(UiTestHost host, string key)
	{
		return host.ById(key).Flag(UiConfigProperties.Value);
	}

	private static Dictionary<string, bool> Visibility(UiTestHost host)
	{
		return _listOnly.ToDictionary(key => key,
			key =>
			{
				if (host.ById(key).Property(UiConfigProperties.VisibleWhen) is not
					{ ValueKind: JsonValueKind.Object } rule)
				{
					return true;
				}

				var sibling = host.ById(rule.GetProperty("parameterName").GetString()!).Text(UiConfigProperties.Value);
				return rule.GetProperty("values").EnumerateArray().Any(value => value.GetString() == sibling);
			});
	}

	private static JsonElement Compose(UiTestHost host, string flows)
	{
		return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
		{
			[EmailWidgetTypes.InboxesKey] = host.ById(EmailWidgetTypes.InboxesKey).Property(UiConfigProperties.Value),
			[EmailWidgetTypes.ModeKey] = host.ById(EmailWidgetTypes.ModeKey).Text(UiConfigProperties.Value),
			[EmailWidgetTypes.UnreadOnlyKey] = Flag(host, EmailWidgetTypes.UnreadOnlyKey),
			[EmailWidgetTypes.MaxMessagesKey]
				= host.ById(EmailWidgetTypes.MaxMessagesKey).Number(UiConfigProperties.Value),
			[EmailWidgetTypes.ShowSenderKey] = Flag(host, EmailWidgetTypes.ShowSenderKey),
			[EmailWidgetTypes.ShowSubjectKey] = Flag(host, EmailWidgetTypes.ShowSubjectKey),
			[EmailWidgetTypes.ShowPreviewKey] = Flag(host, EmailWidgetTypes.ShowPreviewKey),
			[EmailWidgetTypes.ShowTimestampKey] = Flag(host, EmailWidgetTypes.ShowTimestampKey),
			["flows"] = JsonDocument.Parse(flows).RootElement.Clone()
		});
	}

	private static JsonElement Data(object value)
	{
		return JsonSerializer.SerializeToElement(value);
	}
}
