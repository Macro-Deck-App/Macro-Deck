using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions.InProcess;

[TestFixture]
public class InProcessUiSessionProviderFlushTests
{
	[Test]
	public async Task A_flush_queued_before_its_event_ran_completes_only_after_the_patch_that_event_produced()
	{
		var session = new GatedSession();
		var sink = new RecordingSink();
		var provider = new InProcessUiSessionProvider("test-provider",
			new FakeUiProvider(session),
			sink,
			Serilog.Log.Logger);
		await provider.OpenAsync(new UiSessionOpenCommand
			{
				SessionId = "s1",
				Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				UiModelVersion = 1
			},
			CancellationToken.None);

		await provider.DispatchEventAsync("s1",
			new UiSessionEventCommand { NodeId = "label", Name = "change" },
			CancellationToken.None);
		Assert.That(await session.Entered.WaitAsync(TimeSpan.FromSeconds(5)),
			Is.True,
			"The event never reached the session.");

		var flush = provider.FlushAsync("s1", CancellationToken.None);
		session.Flush = flush;
		session.Gate.Release();
		await flush.WaitAsync(TimeSpan.FromSeconds(5));
		await session.Drained.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(sink.Patches, Is.EqualTo(1));
			Assert.That(session.DrainedAfterFlush,
				Is.False,
				"The flush completed before the patch its event produced.");
		});

		await provider.DisposeAsync();
	}

	private sealed class FakeUiProvider : IUiProvider
	{
		private readonly IUiSession _session;

		public FakeUiProvider(IUiSession session)
		{
			_session = session;
		}

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } = [];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(_session);
		}
	}

	private sealed class RecordingSink : IUiSessionSink
	{
		private int _patches;

		public int Patches => Volatile.Read(ref _patches);

		public UiSessionIngestResult PublishSnapshot(string providerId, string sessionId, UiRawJson tree)
		{
			return UiSessionIngestResult.Accept();
		}

		public UiSessionIngestResult PublishPatch(string providerId, string sessionId, UiRawJson patch)
		{
			Interlocked.Increment(ref _patches);
			return UiSessionIngestResult.Accept();
		}

		public void PublishFault(string providerId, string sessionId, string code, string? message)
		{
		}

		public void PublishReload(string providerId, string sessionId)
		{
		}

		public UiSessionIngestResult PublishParameterValues(
			string providerId,
			string sessionId,
			IReadOnlyDictionary<string, JsonElement> values)
		{
			return UiSessionIngestResult.Accept();
		}
	}

	private sealed class GatedSession : IUiSession
	{
		private readonly Lock _gate = new();
		private readonly List<UiPatch> _pending = [];

		public SemaphoreSlim Entered { get; } = new(0);

		public SemaphoreSlim Gate { get; } = new(0);

		public SemaphoreSlim Drained { get; } = new(0);

		public Task? Flush { get; set; }

		public bool DrainedAfterFlush { get; private set; }

		public event EventHandler? Changed;

#pragma warning disable CS0067 // Required by IUiSession; this fake never faults.
		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;
#pragma warning restore CS0067

		public UiTree BuildTree()
		{
			return new UiTree
			{
				Root = new UiNode { Id = "root", Type = "stack" },
				Revision = 1,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			lock (_gate)
			{
				var drained = _pending.ToList();
				_pending.Clear();
				if (drained.Count > 0)
				{
					DrainedAfterFlush = Flush?.IsCompleted == true;
					Drained.Release();
				}

				return drained;
			}
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Entered.Release();
			Gate.Wait(TimeSpan.FromSeconds(5));
			lock (_gate)
			{
				_pending.Add(new UiPatch
				{
					FromRevision = 1,
					ToRevision = 2,
					Operations = [new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "root" }]
				});
			}

			Changed?.Invoke(this, EventArgs.Empty);
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
