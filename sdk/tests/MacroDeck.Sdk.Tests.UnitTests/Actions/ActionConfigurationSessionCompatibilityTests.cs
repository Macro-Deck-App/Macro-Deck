using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class ActionConfigurationSessionCompatibilityTests
{
	[Test]
	public async Task A_configuration_session_written_before_reports_is_still_a_plain_session()
	{
		IUiConfigurableActionDefinition action = new ActionWrittenBeforeReports();

		var session = await action.CreateConfigurationSessionAsync(new ActionConfigurationRequest
			{
				Session = new UiSessionRequest { Surface = ConfigSurface(), UiModelVersion = 1 }
			},
			CancellationToken.None);

		session!.Dispatch(new UiEvent { NodeId = "root", Name = "change" }, new NowhereOrigin());

		Assert.Multiple(() =>
		{
			Assert.That(session, Is.Not.InstanceOf<IActionConfigurationSession>());
			Assert.That(((SessionWrittenBeforeReports)session).Received, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task The_wrapper_forwards_every_session_member_to_the_session_it_wraps()
	{
		var inner = new SessionWrittenBeforeReports();
		var wrapper = new ActionConfigurationSession(inner);
		var origin = new NowhereOrigin();
		var changed = 0;
		var faulted = 0;
		wrapper.Changed += (_, _) => changed++;
		wrapper.Faulted += (_, _) => faulted++;

		var tree = wrapper.BuildTree();
		var patches = wrapper.DrainPatches();
		wrapper.Dispatch(new UiEvent { NodeId = "a", Name = "press" });
		wrapper.Dispatch(new UiEvent { NodeId = "b", Name = "press" }, origin);
		inner.RaiseChanged();
		inner.RaiseFaulted();
		await wrapper.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(tree, Is.SameAs(inner.Tree));
			Assert.That(patches, Is.SameAs(inner.Patches));
			Assert.That(string.Join(",", inner.Received.Select(received => received.NodeId)), Is.EqualTo("a,b"));
			Assert.That(inner.Origins, Is.EqualTo(new IUiEventOrigin?[] { null, origin }));
			Assert.That(changed, Is.EqualTo(1));
			Assert.That(faulted, Is.EqualTo(1));
			Assert.That(inner.Disposed, Is.True);
		});
	}

	[Test]
	public void A_report_is_raised_synchronously_with_the_values_as_reported()
	{
		var wrapper = new ActionConfigurationSession(new SessionWrittenBeforeReports());
		var reported = new List<IReadOnlyDictionary<string, JsonElement>>();
		wrapper.ParameterValuesReported += (_, e) => reported.Add(e.Values);

		wrapper.Report("message", JsonSerializer.SerializeToElement(new { content = "hi" }));
		wrapper.Report(new Dictionary<string, JsonElement>());

		Assert.Multiple(() =>
		{
			Assert.That(reported, Has.Count.EqualTo(1), "An empty report raised the event.");
			Assert.That(reported[0]["message"].GetProperty("content").GetString(), Is.EqualTo("hi"));
		});
	}

	private static UiSurface ConfigSurface()
	{
		return new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };
	}

	private sealed class ActionWrittenBeforeReports : IUiConfigurableActionDefinition
	{
		public string Id => "legacy";

		public LocalizedText Name => "Legacy";

		public LocalizedText Description => "Configured before reports existed.";

		public IReadOnlyList<ActionParameter> Parameters => [];

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<IUiSession?> CreateConfigurationSessionAsync(
			ActionConfigurationRequest request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(new SessionWrittenBeforeReports());
		}
	}

	private sealed class SessionWrittenBeforeReports : IUiSession
	{
		public UiTree Tree { get; } = new()
		{
			Revision = 1, Surface = ConfigSurface(), Root = new UiNode { Id = "root", Type = "stack" }
		};

		public IReadOnlyList<UiPatch> Patches { get; } = [];

		public List<UiEvent> Received { get; } = [];

		public List<IUiEventOrigin?> Origins { get; } = [];

		public bool Disposed { get; private set; }

		public event EventHandler? Changed;

		public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

		public UiTree BuildTree()
		{
			return Tree;
		}

		public IReadOnlyList<UiPatch> DrainPatches()
		{
			return Patches;
		}

		public void Dispatch(UiEvent uiEvent)
		{
			Received.Add(uiEvent);
			Origins.Add(null);
		}

		public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
		{
			Received.Add(uiEvent);
			Origins.Add(origin);
		}

		public void RaiseChanged()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public void RaiseFaulted()
		{
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("gone"));
		}

		public ValueTask DisposeAsync()
		{
			Disposed = true;
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
