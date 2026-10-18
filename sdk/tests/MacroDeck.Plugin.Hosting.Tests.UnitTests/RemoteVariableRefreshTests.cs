using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

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
	public async Task A_definition_refresh_names_its_ids_on_the_refresh_operation_every_host_knows()
	{
		var host = new GatedHost();
		host.Release();

		await new RemoteVariableApi(host).RequestDefinitionRefreshAsync(["muted", "volume-percent"]);

		Assert.Multiple(() =>
		{
			Assert.That(host.Operations, Is.EqualTo(new[] { HostOperations.Variables.Refresh }));
			Assert.That(host.Arguments, Is.EqualTo(new[] { """{"definitionIds":["muted","volume-percent"]}""" }));
		});
	}

	[Test]
	public async Task A_definition_refresh_naming_nothing_sends_nothing()
	{
		var host = new GatedHost();
		host.Release();

		await new RemoteVariableApi(host).RequestDefinitionRefreshAsync([]);

		Assert.That(host.Operations, Is.Empty);
	}

	[Test]
	public async Task Definition_refreshes_queued_behind_one_on_its_way_are_merged_into_one_request()
	{
		var host = new GatedHost();
		var api = new RemoteVariableApi(host);

		var first = api.RequestRefreshAsync();
		await host.WaitForCallsAsync(1);
		var second = api.RequestDefinitionRefreshAsync(["muted"]);
		var third = api.RequestDefinitionRefreshAsync(["volume-percent", "muted"]);
		host.Release();
		await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(host.Arguments, Is.EqualTo(new[] { null, """{"definitionIds":["muted","volume-percent"]}""" }));
	}

	[Test]
	public async Task A_refresh_of_every_declared_variable_still_sends_the_ids_queued_with_it_for_catalog_resources()
	{
		var host = new GatedHost();
		var api = new RemoteVariableApi(host);

		var first = api.RequestDefinitionRefreshAsync(["muted"]);
		await host.WaitForCallsAsync(1);
		var second = api.RequestDefinitionRefreshAsync(["input/mic/volume"]);
		var third = api.RequestRefreshAsync();
		var fourth = api.RequestDefinitionRefreshAsync(["cpu"]);
		host.Release();
		await Task.WhenAll(first, second, third, fourth).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(host.Arguments,
			Is.EqualTo(new[]
			{
				"""{"definitionIds":["muted"]}""", null, """{"definitionIds":["cpu","input/mic/volume"]}"""
			}));
	}

	[Test]
	public async Task More_ids_than_one_request_may_carry_are_sent_in_several_requests_instead_of_being_dropped()
	{
		var host = new GatedHost();
		host.Release();
		var ids = Enumerable.Range(0, ProtocolLimits.MaxVariableRefreshDefinitionIds + 1)
			.Select(index => $"input/{index:D5}/muted")
			.ToList();

		await new RemoteVariableApi(host).RequestDefinitionRefreshAsync(ids);

		var sent = host.Arguments
			.Select(arguments
				=> JsonSerializer.Deserialize<VariablesRefreshArguments>(arguments!, PluginProtocolJson.Options)!)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(sent.Select(arguments => arguments.DefinitionIds!.Count),
				Is.EqualTo(new[] { ProtocolLimits.MaxVariableRefreshDefinitionIds, 1 }));
			Assert.That(sent.SelectMany(arguments => arguments.DefinitionIds!), Is.EquivalentTo(ids));
		});
	}

	[Test]
	public void A_definition_refresh_against_a_macro_deck_without_refresh_is_a_no_op()
	{
		var host = new GatedHost
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};
		host.Release();

		Assert.DoesNotThrowAsync(() => new RemoteVariableApi(host).RequestDefinitionRefreshAsync(["muted"]));
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

		public void CatalogChanged(string kind, string? localId = null, string? reason = null)
		{
			Kinds.Add(kind);
		}
	}

	private sealed class GatedHost : IHostInvoker
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly Lock _gate = new();
		private readonly List<string> _operations = [];
		private readonly List<string?> _arguments = [];

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

		public IReadOnlyList<string?> Arguments
		{
			get
			{
				lock (_gate)
				{
					return [.. _arguments];
				}
			}
		}

		public void Release()
		{
			_released.TrySetResult();
		}

		public async Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_operations.Add(operation);
				_arguments.Add(arguments is null
					? null
					: JsonSerializer.Serialize(arguments, PluginProtocolJson.Options));
			}

			await _released.Task.ConfigureAwait(false);
			if (Failure is { } failure)
			{
				throw failure;
			}

			return null;
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}

		public async Task WaitForCallsAsync(int count)
		{
			for (var attempt = 0; attempt < 500 && Operations.Count < count; attempt++)
			{
				await Task.Delay(10);
			}
		}
	}
}
