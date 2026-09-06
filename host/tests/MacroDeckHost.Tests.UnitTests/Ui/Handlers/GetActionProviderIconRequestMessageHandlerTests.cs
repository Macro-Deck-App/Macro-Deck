using System.Text.Json;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Handlers;

/// <summary>
/// <c>POST /api/actions/provider-icon</c> (issue #425), modelled on
/// <see cref="GetActionProviderStatesRequestMessageHandler" />. Resolved decision 9 requires the probe to
/// keep four answers distinct: a snapshot naming a reference, a snapshot with <c>NoIcon</c>, the provider
/// answering <c>null</c>, and a transport-level failure (the action does not exist or is not an icon
/// provider) - never collapsed into each other.
/// </summary>
[TestFixture]
public class GetActionProviderIconRequestMessageHandlerTests
{
	// Acceptance Group E, scenario 21: the editor probe tolerates a half-typed draft and never executes
	// the action - every call answers without an error ("no snapshot" is a valid answer, not an error),
	// GetActionIconAsync is called every time without throwing, and the action's execute counter stays 0.
	[Test]
	public async Task AHalfTypedDraft_IsAnsweredRepeatedlyWithoutError_AndNeverExecutesTheAction()
	{
		var action = new FakeIconProviderAction { Id = "current-track" };
		var handler = CreateHandler(action);
		var request = new GetActionProviderIconRequest
		{
			IntegrationId = "spotify",
			ActionId = "current-track",
			Parameters = new Dictionary<string, JsonElement>
			{
				["playerId"] = JsonSerializer.SerializeToElement((string?)null),
				["size"] = JsonSerializer.SerializeToElement("la")
			}
		};

		for (var i = 0; i < 3; i++)
		{
			var response = await handler.Handle(request, CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(response.Error, Is.Null, "'no snapshot' is a valid answer, not an error");
				Assert.That(response.HasSnapshot, Is.False);
			});
		}

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionIconCallCount, Is.EqualTo(3));
			Assert.That(action.ExecuteCount, Is.EqualTo(0), "the probe never runs the action's side effect");
		});
	}

	// Resolved decision 9: the probe keeps a reference-bearing snapshot, a NoIcon snapshot, a null
	// snapshot, and a transport failure as four distinct answers.
	[Test]
	public async Task TheFourAnswersStayDistinct_ReferenceNoIconNullAndTransportFailure()
	{
		var action = new FakeIconProviderAction { Id = "current-track" };
		var handler = CreateHandler(action);
		var request = new GetActionProviderIconRequest { IntegrationId = "spotify", ActionId = "current-track" };

		action.SnapshotToReturn = new ActionIconSnapshot
		{
			Version = "v1", Reference = ActionIconReference.IconPack("0198aaaa-1111-2222-3333-444444444444")
		};
		var withReference = await handler.Handle(request, CancellationToken.None);

		action.SnapshotToReturn = new ActionIconSnapshot { NoIcon = true };
		var withNoIcon = await handler.Handle(request, CancellationToken.None);

		action.SnapshotToReturn = null;
		var withNull = await handler.Handle(request, CancellationToken.None);

		var notFound = await handler.Handle(new GetActionProviderIconRequest
				{ IntegrationId = "spotify", ActionId = "not-an-icon-provider" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(withReference.Error, Is.Null);
			Assert.That(withReference.HasSnapshot, Is.True);
			Assert.That(withReference.NoIcon, Is.False);
			Assert.That(withReference.Reference?.Reference, Is.EqualTo("0198aaaa-1111-2222-3333-444444444444"));

			Assert.That(withNoIcon.Error, Is.Null);
			Assert.That(withNoIcon.HasSnapshot, Is.True);
			Assert.That(withNoIcon.NoIcon, Is.True);
			Assert.That(withNoIcon.Reference, Is.Null);

			Assert.That(withNull.Error, Is.Null);
			Assert.That(withNull.HasSnapshot, Is.False, "the provider answered null - a well-formed non-error answer");

			Assert.That(notFound.Error,
				Is.Not.Null,
				"an action that is not an icon provider is a transport-level failure");
			Assert.That(notFound.HasSnapshot, Is.False);
		});
	}

	private static GetActionProviderIconRequestMessageHandler CreateHandler(FakeIconProviderAction action)
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		return new GetActionProviderIconRequestMessageHandler(registry,
			new RemoteIconProviderActionRegistry(new EmptySnapshotStore(), new UnusedInvoker(), new EmptyAssetCache()),
			Serilog.Log.Logger);
	}

	private sealed class EmptySnapshotStore : IRemotePluginSnapshotStore
	{
		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId) =>
			RemotePluginCapabilitySnapshot.Empty(pluginId);

		public bool Has(string pluginId) => false;

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class UnusedInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("No test in this file resolves through the remote registry.");

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope result)
			=> throw new NotSupportedException();

		public void AbortAll(string pluginId, MacroDeck.Plugin.Protocol.Errors.ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class EmptyAssetCache : IPluginAssetCache
	{
		public void Write(string contentHash, string mimeType, byte[] bytes)
			=> throw new NotSupportedException();

		public bool TryRead(string contentHash, out byte[] bytes, out string mimeType)
			=> throw new NotSupportedException();
	}

	private sealed class FakeIconProviderAction : IActionDefinition, IIconProviderActionDefinition
	{
		public string Id { get; init; } = "provide-icon";
		public MacroDeck.Localization.LocalizedText Name { get; init; } = "Provide Icon";
		public MacroDeck.Localization.LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

		public ActionIconSnapshot? SnapshotToReturn { get; set; }
		public ActionResult Result { get; set; } = ActionResult.Success();

		public int ExecuteCount { get; private set; }
		public int GetActionIconCallCount { get; private set; }

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			GetActionIconCallCount++;
			return Task.FromResult(SnapshotToReturn);
		}

		public IActionExecutor CreateExecutor() => new Executor(this);

		private sealed class Executor : IActionExecutor
		{
			private readonly FakeIconProviderAction _owner;

			public Executor(FakeIconProviderAction owner) => _owner = owner;

			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				_owner.ExecuteCount++;
				return Task.FromResult(_owner.Result);
			}
		}
	}
}
