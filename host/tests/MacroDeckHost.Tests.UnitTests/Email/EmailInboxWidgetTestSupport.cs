using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.EmailInbox;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Email;

internal sealed class EmailInboxWidgetHarness : IDisposable
{
	public const string WorkRef = "app.imap::work";

	public const string HomeRef = "app.other::home";

	public EmailInboxWidgetHarness()
	{
		Imap = new FakeEmailIntegration("app.imap", "IMAP").WithInbox("work", "Work");
		Other = new FakeEmailIntegration("app.other", "Other Mail").WithInbox("home", "Home");
		Integrations = new ConfigurableIntegrationRegistry([Imap, Other]);
		World = new EmailWorld(Integrations);
		Provider = CreateProvider(World);
	}

	public FakeEmailIntegration Imap { get; }

	public FakeEmailIntegration Other { get; }

	public ConfigurableIntegrationRegistry Integrations { get; }

	public EmailWorld World { get; }

	public EmailInboxUiProvider Provider { get; }

	public Task SyncAsync()
	{
		return World.SyncAllAsync();
	}

	public static EmailInboxUiProvider CreateProvider(EmailWorld world)
	{
		return new EmailInboxUiProvider(world.Cache,
			new UiResourceStore(),
			world.Time,
			CalendarWidgetHarness.ScopeFactory(),
			TestLocalization.SampleText,
			Logger.None,
			TimeZoneInfo.Utc);
	}

	public static async Task<IUiSession> OpenWidgetAsync(
		EmailInboxUiProvider provider,
		object data,
		bool sample = false)
	{
		return (await provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = CalendarWidgetHarness.WidgetSurface(EmailWidgetTypes.QualifiedId, data, sample),
				UiModelVersion = 1
			},
			CancellationToken.None))!;
	}

	public Task<IUiSession> OpenWidgetAsync(object data, bool sample = false)
	{
		return OpenWidgetAsync(Provider, data, sample);
	}

	public async Task<IUiSession> OpenConfigAsync(object data)
	{
		return (await Provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = CalendarWidgetHarness.ConfigSurface(EmailWidgetTypes.QualifiedId, data),
				UiModelVersion = 1
			},
			CancellationToken.None))!;
	}

	public void Dispose()
	{
		World.Dispose();
	}
}

internal static class EmailInboxTree
{
	public static UiNode Layout(IUiSession session, string layout)
	{
		return CalendarTree.Get(CalendarTree.Get(session.BuildTree().Root, "layout"), layout);
	}

	public const double AllRoom = 100;

	public static List<UiNode> Rows(UiNode root, double heightCells = AllRoom)
	{
		return [.. Drawn(root, heightCells).Where(node => node.Id.EndsWith(".row", StringComparison.Ordinal))];
	}

	public static IEnumerable<UiNode> Drawn(UiNode node, double heightCells)
	{
		yield return node;

		IEnumerable<UiNode> children = node.Type == UiComponents.Responsive
			?
			[
				node.Children[UiResponsiveSelection.SelectChild(node.Properties[UiComponentProperties.Variants],
					node.Children.Count,
					AllRoom,
					heightCells)]
			]
			: node.Children;

		foreach (var child in children.SelectMany(child => Drawn(child, heightCells)))
		{
			yield return child;
		}
	}

	public static string? Text(UiNode row, string key)
	{
		return CalendarTree.Find(row, key) is { } node ? CalendarTree.Text(node) : null;
	}
}
