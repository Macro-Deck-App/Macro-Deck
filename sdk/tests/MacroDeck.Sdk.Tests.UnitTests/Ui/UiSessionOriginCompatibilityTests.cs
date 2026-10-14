using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Sdk.Tests.UnitTests.Ui;

[TestFixture]
public class UiSessionOriginCompatibilityTests
{
	[Test]
	public void A_session_written_before_origins_receives_the_event_dispatched_with_an_origin()
	{
		var session = new SessionWrittenBeforeOrigins();
		IUiSession dispatched = session;

		dispatched.Dispatch(new UiEvent { NodeId = "root", Name = "press" }, new NowhereOrigin());

		Assert.That(session.Received.Single().Name, Is.EqualTo("press"));
	}

	private sealed class SessionWrittenBeforeOrigins : IUiSession
	{
		public List<UiEvent> Received { get; } = [];

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
				Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				Root = new UiNode { Id = "root", Type = "stack" }
			};
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return [];
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Received.Add(uiEvent);
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	private sealed class NowhereOrigin : IUiEventOrigin
	{
		public string? ClientId => null;

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}
}
