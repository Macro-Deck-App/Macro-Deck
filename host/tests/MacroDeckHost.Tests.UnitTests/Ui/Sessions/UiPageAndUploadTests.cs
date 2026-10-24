using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiPageAndUploadTests : UiSessionFixture
{
	private const string PageProviderId = "com.example.pages";

	private PageUiSessionOpener _pages = null!;
	private UiUploadStore _uploads = null!;

	[SetUp]
	public void PageSetUp()
	{
		_pages = new PageUiSessionOpener(Integrations,
			new EmptyRemotePluginSnapshotStore(),
			Broker,
			new NoPluginConnections());
		_uploads = new UiUploadStore(Registry, Time);
	}

	[TearDown]
	public void PageTearDown()
	{
		_uploads.Dispose();
	}

	[Test]
	public void A_device_cannot_open_an_integration_page()
	{
		AddPageProvider(UiSurfaceKinds.Page);

		var ticket = _pages.Open(Request("scripts"), DeviceA, false);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.False);
			Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
		});
	}

	[Test]
	public async Task An_admin_opens_a_declared_page_on_a_page_surface_naming_the_integration_and_the_page()
	{
		AddPageProvider(UiSurfaceKinds.Page);

		var ticket = await _pages.Open(Request("scripts"), string.Empty, true).Ready;

		var surface = Registry.Find(ticket.SessionId)!.Surface;
		Assert.Multiple(() =>
		{
			Assert.That(ticket.Accepted, Is.True);
			Assert.That(surface.Kind, Is.EqualTo("page"));
			Assert.That(surface.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(surface.Attributes["integrationId"].GetString(), Is.EqualTo(PageProviderId));
			Assert.That(surface.Attributes["pageId"].GetString(), Is.EqualTo("scripts"));
		});
	}

	[Test]
	public void A_page_that_was_not_declared_does_not_open()
	{
		AddPageProvider(UiSurfaceKinds.Page);

		var ticket = _pages.Open(Request("other"), string.Empty, true);

		Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
	}

	[Test]
	public void Pages_of_a_provider_without_a_page_surface_do_not_open()
	{
		AddPageProvider(UiSurfaceKinds.Widget);

		var ticket = _pages.Open(Request("scripts"), string.Empty, true);

		Assert.That(ticket.Code, Is.EqualTo(UiSessionErrorCodes.ProviderUnavailable));
	}

	[Test]
	public async Task An_upload_is_held_for_the_owner_and_hashed_as_a_ui_resource_is()
	{
		var sessionId = await OpenPageAsync();
		var text = "MsgBox \"hi\""u8.ToArray();

		var foreign = _uploads.Add(sessionId, "editor", DeviceB, text);
		var stored = _uploads.Add(sessionId, "editor", string.Empty, text);

		Assert.Multiple(() =>
		{
			Assert.That(foreign.Outcome, Is.EqualTo(UiUploadOutcome.NoSuchSession));
			Assert.That(stored.Outcome, Is.EqualTo(UiUploadOutcome.Stored));
			Assert.That(stored.Upload!.UploadId, Has.Length.EqualTo(32));
			Assert.That(stored.Upload.ContentHash, Is.EqualTo(AssetContentHash.Compute(text)));
			Assert.That(_uploads.Read(PageProviderId, sessionId, stored.Upload.UploadId)!.Content.ToArray(),
				Is.EqualTo(text));
		});
	}

	[Test]
	public async Task Only_the_provider_serving_the_session_reads_its_upload()
	{
		var sessionId = await OpenPageAsync();
		var upload = _uploads.Add(sessionId, "editor", string.Empty, "x"u8.ToArray()).Upload!;

		Assert.That(_uploads.Read("com.example.other", sessionId, upload.UploadId), Is.Null);
	}

	[Test]
	public async Task A_newer_upload_of_the_same_input_replaces_the_older_one()
	{
		var sessionId = await OpenPageAsync();
		var older = _uploads.Add(sessionId, "editor", string.Empty, "1"u8.ToArray()).Upload!;

		var newer = _uploads.Add(sessionId, "editor", string.Empty, "2"u8.ToArray()).Upload!;

		Assert.Multiple(() =>
		{
			Assert.That(_uploads.Read(PageProviderId, sessionId, older.UploadId), Is.Null);
			Assert.That(_uploads.Read(PageProviderId, sessionId, newer.UploadId), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_session_holds_at_most_four_pending_uploads()
	{
		var sessionId = await OpenPageAsync();
		for (var node = 0; node < UiUploadStore.MaxPendingPerSession; node++)
		{
			_uploads.Add(sessionId, $"editor-{node}", string.Empty, "x"u8.ToArray());
		}

		var fifth = _uploads.Add(sessionId, "editor-4", string.Empty, "x"u8.ToArray());

		Assert.That(fifth.Outcome, Is.EqualTo(UiUploadOutcome.Full));
	}

	[Test]
	public async Task An_upload_expires_after_five_minutes()
	{
		var sessionId = await OpenPageAsync();
		Attach(sessionId, "connection-1", string.Empty);
		var upload = _uploads.Add(sessionId, "editor", string.Empty, "x"u8.ToArray()).Upload!;

		Time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
		var beforeExpiry = _uploads.Read(PageProviderId, sessionId, upload.UploadId);
		Time.Advance(TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(beforeExpiry, Is.Not.Null);
			Assert.That(_uploads.Read(PageProviderId, sessionId, upload.UploadId), Is.Null);
		});
	}

	[Test]
	public async Task Closing_the_session_discards_its_uploads()
	{
		var sessionId = await OpenPageAsync();
		var upload = _uploads.Add(sessionId, "editor", string.Empty, "x"u8.ToArray()).Upload!;

		await Broker.CloseAsync(sessionId, "closed", CancellationToken.None);

		Assert.That(_uploads.Read(PageProviderId, sessionId, upload.UploadId), Is.Null);
	}

	[Test]
	public async Task An_in_process_origin_reads_uploads_of_its_own_session_only()
	{
		var sessionId = await OpenPageAsync();
		var otherSessionId = await OpenPageAsync();
		var text = "Run \"notepad\""u8.ToArray();
		var upload = _uploads.Add(sessionId, "editor", string.Empty, text).Upload!;
		var origins = new UiEventOrigins(Broker, Transport, Interactions, ClientFeatures, _uploads);

		var read = await origins.Create(PageProviderId, sessionId, "admin", "content-uploaded")
			.ReadUploadAsync(upload.UploadId);

		Assert.Multiple(() =>
		{
			Assert.That(read.ToArray(), Is.EqualTo(text));
			Assert.ThrowsAsync<UiUploadNotFoundException>(() => origins
				.Create(PageProviderId, otherSessionId, "admin", "content-uploaded")
				.ReadUploadAsync(upload.UploadId));
		});
	}

	private static OpenPageUiSessionRequest Request(string pageId)
	{
		return new OpenPageUiSessionRequest { IntegrationId = PageProviderId, PageId = pageId };
	}

	private void AddPageProvider(string surfaceKind)
	{
		Integrations.Add(new PageIntegration(surfaceKind));
	}

	private async Task<string> OpenPageAsync()
	{
		if (Integrations.Integrations.All(integration => integration.Id != PageProviderId))
		{
			AddPageProvider(UiSurfaceKinds.Page);
		}

		var ticket = await _pages.Open(Request("scripts"), string.Empty, true).Ready;
		Assert.That(ticket.Accepted, Is.True, "The page did not open.");
		return ticket.SessionId;
	}

	private sealed class PageIntegration(string surfaceKind) : IIntegration, IUiProvider
	{
		public string Id => PageProviderId;

		public LocalizedText Name => "Pages";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = surfaceKind, SessionMode = UiSessionModes.Exclusive }];

		public IReadOnlyList<UiPageDeclaration> Pages => [new() { Id = "scripts", Title = "Scripts" }];

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
			return Task.FromResult<IUiSession?>(new PageSession());
		}
	}

	private sealed class PageSession : IUiSession
	{
		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted
		{
			add { }
			remove { }
		}

		public UiTree BuildTree()
		{
			return new UiTree
			{
				Revision = 1,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive },
				Root = new UiNode { Id = "root", Type = "panel" }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
