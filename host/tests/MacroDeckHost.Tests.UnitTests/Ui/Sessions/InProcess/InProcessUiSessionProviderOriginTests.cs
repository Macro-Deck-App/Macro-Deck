using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions.InProcess;

/// <summary>
/// Pins issue #748's origin-identity fix: <see cref="InProcessUiSessionProvider" /> must hand the acting
/// client id from <see cref="UiSessionEventCommand.ClientId" /> to a session that asks for it through
/// <see cref="IOriginAwareUiSession" />, rather than discarding it - the gap that made a folder-change
/// press broadcast to every connected device instead of the one pressed.
/// </summary>
[TestFixture]
public class InProcessUiSessionProviderOriginTests
{
	[Test]
	public async Task An_origin_aware_session_receives_the_dispatching_clients_id()
	{
		var session = new RecordingSession();
		var provider = new InProcessUiSessionProvider("test-provider",
			new FakeUiProvider(session),
			new FakeUiSessionSink(),
			Serilog.Log.Logger);

		await provider.OpenAsync(new UiSessionOpenCommand
			{
				SessionId = "s1",
				Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				UiModelVersion = 1,
			},
			CancellationToken.None);

		await provider.DispatchEventAsync("s1",
			new UiSessionEventCommand { NodeId = "actionButton", Name = "press", ClientId = "client-42" },
			CancellationToken.None);

		await session.Received.WaitAsync(TimeSpan.FromSeconds(2));

		Assert.That(session.LastOriginClientId, Is.EqualTo("client-42"));

		await provider.DisposeAsync();
	}

	[Test]
	public async Task A_plain_session_that_does_not_implement_origin_awareness_is_still_dispatched_to()
	{
		var session = new PlainRecordingSession();
		var provider = new InProcessUiSessionProvider("test-provider",
			new FakeUiProvider(session),
			new FakeUiSessionSink(),
			Serilog.Log.Logger);

		await provider.OpenAsync(new UiSessionOpenCommand
			{
				SessionId = "s1",
				Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				UiModelVersion = 1,
			},
			CancellationToken.None);

		await provider.DispatchEventAsync("s1",
			new UiSessionEventCommand { NodeId = "n", Name = "press", ClientId = "client-1" },
			CancellationToken.None);

		await session.Received.WaitAsync(TimeSpan.FromSeconds(2));

		Assert.That(session.DispatchCount, Is.EqualTo(1));

		await provider.DisposeAsync();
	}

	private sealed class FakeUiProvider : IUiProvider
	{
		private readonly IUiSession _session;

		public FakeUiProvider(IUiSession session) => _session = session;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } = [];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult<IUiSession?>(_session);
	}

	private sealed class FakeUiSessionSink : IUiSessionSink
	{
		public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
			=> UiSessionIngestResult.Accept();

		public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
			=> UiSessionIngestResult.Accept();

		public void PublishFault(string providerId, string sessionId, string code, string? message)
		{
		}
	}

	private sealed class RecordingSession : IUiSession, IOriginAwareUiSession
	{
		public SemaphoreSlim Received { get; } = new(0);

		public string? LastOriginClientId { get; private set; }

#pragma warning disable CS0067 // Required by IUiSession; this fake never raises either.
		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;
#pragma warning restore CS0067

		public UiTree BuildTree() => new()
		{
			Root = new UiNode { Id = "root", Type = "stack" },
			Revision = 0,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		};

		public IReadOnlyList<UiPatch> DrainPatches() => [];

		public void Dispatch(UiEvent uiEvent) => Dispatch(uiEvent, null);

		public void Dispatch(UiEvent uiEvent, string? originClientId)
		{
			LastOriginClientId = originClientId;
			Received.Release();
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class PlainRecordingSession : IUiSession
	{
		public SemaphoreSlim Received { get; } = new(0);

		public int DispatchCount { get; private set; }

#pragma warning disable CS0067 // Required by IUiSession; this fake never raises either.
		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;
#pragma warning restore CS0067

		public UiTree BuildTree() => new()
		{
			Root = new UiNode { Id = "root", Type = "stack" },
			Revision = 0,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		};

		public IReadOnlyList<UiPatch> DrainPatches() => [];

		public void Dispatch(UiEvent uiEvent)
		{
			DispatchCount++;
			Received.Release();
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
