using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiPageSelectionTests : UiSessionFixture
{
	private const string LibraryId = "com.example.library";
	private const string SecondItemId = "page.rail.items.second";

	private PageUiSessionOpener _pages = null!;
	private LibraryIntegration _library = null!;

	[SetUp]
	public void SelectionSetUp()
	{
		_library = new LibraryIntegration();
		Integrations.Add(_library);
		_pages = new PageUiSessionOpener(Integrations,
			new EmptyRemotePluginSnapshotStore(),
			Broker,
			new NoPluginConnections());
	}

	[Test]
	public async Task Selecting_in_one_clients_page_leaves_the_other_clients_page_unchanged()
	{
		var first = await OpenAsync();
		var second = await OpenAsync();
		Assert.That(Attach(first, "connection-1", string.Empty).Accepted, Is.True);
		Assert.That(Attach(second, "connection-2", string.Empty).Accepted, Is.True);

		Broker.SendEvent(new UiSendEventRequest { SessionId = first, NodeId = SecondItemId, Name = "activate" },
			"connection-1",
			"client-1");
		await WaitForAsync(() => IsSelected(_library.Sessions[0]), "The activate never selected the row.");

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.EqualTo(second));
			Assert.That(_library.Sessions, Has.Count.EqualTo(2));
			Assert.That(IsSelected(_library.Sessions[0]), Is.True);
			Assert.That(IsSelected(_library.Sessions[1]), Is.False);
		});
	}

	private async Task<string> OpenAsync()
	{
		var ticket = await _pages.Open(new OpenPageUiSessionRequest { IntegrationId = LibraryId, PageId = "items" },
				string.Empty,
				true)
			.Ready;
		Assert.That(ticket.Accepted, Is.True, "The page did not open.");
		return ticket.SessionId;
	}

	private static bool IsSelected(UiViewSession session)
	{
		return Find(session.View.Tree.Root, SecondItemId) is { } item
			&& item.Properties.TryGetValue("selected", out var selected)
			&& selected.GetBoolean();
	}

	private static UiNode? Find(UiNode node, string id)
	{
		return node.Id == id
			? node
			: node.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null);
	}

	private sealed class LibraryIntegration : IIntegration, IUiProvider
	{
		public List<UiViewSession> Sessions { get; } = [];

		public string Id => LibraryId;

		public LocalizedText Name => "Library";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive }];

		public IReadOnlyList<UiPageDeclaration> Pages => [new() { Id = "items", Title = "Items" }];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			var selected = new UiState<string>(string.Empty);
			var view = new UiView(request.Surface,
				new UiSplit
				{
					Key = "page",
					RailTitle = "Items",
					Rail =
					[
						new UiConfigList
						{
							Key = "items",
							Children = [Item("first", selected), Item("second", selected)]
						}
					],
					Detail = [new UiEmptyState { Key = "nothing", Title = "Nothing selected" }]
				});
			var session = new UiViewSession(view);
			Sessions.Add(session);

			return Task.FromResult<IUiSession?>(session);
		}

		private static UiConfigListItem Item(string key, UiState<string> selected)
		{
			return new UiConfigListItem
			{
				Key = key,
				Label = key,
				Selected = UiValue.From(() => selected.Value == key),
				Events = [UiEventHandler.On(UiConfigEvents.Activate, () => selected.Value = key)]
			};
		}
	}
}
