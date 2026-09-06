using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// A plugin compiled before developer previews existed still declares a <c>ui</c> capability the way it
/// always did: its <c>ui/describe</c> reply carries no <c>previews</c> key at all, written as literal wire
/// JSON for the same reason <see cref="ConfigUiCompatibilityContractTests" /> does - a default on today's
/// DTO proves nothing about the bytes a plugin built years ago actually sends.
/// </summary>
[TestFixture]
internal sealed class UiPreviewCompatibilityContractTests : Harness.CapabilityContractFixture
{
	private static readonly CapabilityVersionRange _v1 = new() { Minimum = 1, Maximum = 1 };

	private const string LegacyUiDescribeJson =
		"""{"surfaces":[{"kind":"widget","sessionMode":"shared"}],"uiModelVersion":2}""";

	[Test]
	public async Task An_old_plugins_ui_capability_is_unaffected_by_previews()
	{
		var invocations = new List<string>();
		var uiHandler = new LegacyHandler(invocations);

		await ConnectAsync([uiHandler],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Ui, LocalId = ProviderCapabilityId.LocalId, VersionRange = _v1
				}
			],
			[CapabilityKinds.Ui]);

		Assert.That(SnapshotStore.Has(PluginId), Is.True, "The legacy plugin's describe reply was not accepted.");
		var snapshot = SnapshotStore.GetSnapshot(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.UiSurfaces, Has.Count.EqualTo(1));
			Assert.That(snapshot.UiSurfaces[0].Kind, Is.EqualTo(UiSurfaceKinds.Widget));
			Assert.That(snapshot.UiSurfaces[0].SessionMode, Is.EqualTo(UiSessionModes.Shared));
			Assert.That(snapshot.UiPreviews, Is.Empty, "A legacy describe payload must not manufacture previews.");
		});

		var listing = new ListUiPreviewsRequestMessageHandler([], IntegrationRegistry, SnapshotStore);
		var response = await listing.Handle(new ListUiPreviewsRequest(), CancellationToken.None);
		Assert.That(response.Previews, Is.Empty, "The legacy plugin contributed to the preview listing.");

		Assert.That(invocations.Contains($"{CapabilityKinds.Ui}/{CapabilityOperations.Ui.SessionOpen}"),
			Is.False,
			"The host tried to open a developer-preview session on a plugin that declares none.");
	}

	/// <summary>One capability exactly as a plugin predating previews implements it: it answers
	/// <c>describe</c> with the old payload shape, byte for byte, and knows nothing about a
	/// <c>previews</c> key.</summary>
	private sealed class LegacyHandler(List<string> invocations) : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Ui;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities() => [];

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			invocations.Add($"{invocation.Kind}/{invocation.Operation}");

			if (string.Equals(invocation.Operation, CapabilityOperations.Ui.Describe, StringComparison.Ordinal))
			{
				return Task.FromResult(
					CapabilityInvocationResult.Ok(JsonDocument.Parse(LegacyUiDescribeJson).RootElement.Clone()));
			}

			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The ui capability has no operation '{invocation.Operation}'."));
		}
	}
}
