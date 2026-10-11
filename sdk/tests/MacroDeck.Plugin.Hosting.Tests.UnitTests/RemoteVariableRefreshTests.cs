using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteVariableRefreshTests
{
	[Test]
	public async Task A_refresh_request_reaches_the_host()
	{
		var host = new GatedHost();
		host.Release();

		await new RemoteVariableApi(host).RequestRefreshAsync();

		Assert.That(host.Operations, Is.EqualTo(new[] { HostOperations.Variables.Refresh }));
	}

	[Test]
	public async Task Requests_made_while_one_is_on_its_way_are_merged_into_one_more()
	{
		var host = new GatedHost();
		var api = new RemoteVariableApi(host);

		var first = api.RequestRefreshAsync();
		await host.WaitForCallsAsync(1);
		var merged = Enumerable.Range(0, 10).Select(_ => api.RequestRefreshAsync()).ToArray();
		host.Release();
		await Task.WhenAll([first, .. merged]).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(host.Operations, Has.Count.EqualTo(2));
	}

	[Test]
	public void A_macro_deck_without_the_request_makes_it_a_no_op()
	{
		var host = new GatedHost
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};
		host.Release();

		Assert.DoesNotThrowAsync(() => new RemoteVariableApi(host).RequestRefreshAsync());
	}

	[Test]
	public async Task A_changed_declared_set_is_reported_as_a_state_update_for_variables()
	{
		var notifier = new RecordingCatalogNotifier();

		await new RemoteVariableApi(new GatedHost(), notifier).NotifyDeclarationsChangedAsync();

		Assert.That(notifier.Kinds, Is.EqualTo(new[] { CapabilityKinds.Variables }));
	}

	private sealed class RecordingCatalogNotifier : IPluginCatalogNotifier
	{
		public List<string> Kinds { get; } = [];

		public void CatalogChanged(string kind, string? localId = null, string? reason = null) => Kinds.Add(kind);
	}

	private sealed class GatedHost : IHostInvoker
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly Lock _gate = new();
		private readonly List<string> _operations = [];

		public Exception? Failure { get; init; }

		public IReadOnlyList<string> Operations
		{
			get
			{
				lock (_gate)
				{
					return [.. _operations];
				}
			}
		}

		public void Release() => _released.TrySetResult();

		public async Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_operations.Add(operation);
			}

			await _released.Task.ConfigureAwait(false);
			if (Failure is { } failure)
			{
				throw failure;
			}

			return null;
		}

		public bool TryComplete(ProtocolEnvelope result) => false;

		public async Task WaitForCallsAsync(int count)
		{
			for (var attempt = 0; attempt < 500 && Operations.Count < count; attempt++)
			{
				await Task.Delay(10);
			}
		}
	}
}
