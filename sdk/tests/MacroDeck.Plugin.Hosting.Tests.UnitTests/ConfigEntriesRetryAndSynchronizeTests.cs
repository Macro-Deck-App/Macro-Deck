using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ConfigEntriesRetryAndSynchronizeTests
{
	[Test]
	public async Task A_repeated_prepare_hands_the_rejected_names_over_and_returns_the_retry_request()
	{
		var plugin = new RetryingIntegration();
		var handler = new ConfigEntriesCapabilityHandler([plugin]);

		var result = await PrepareAsync(handler,
			new ConfigEntryPrepareArguments
			{
				EntryId = Guid.NewGuid(),
				Title = "Home",
				Values = new Dictionary<string, JsonElement>(),
				RejectedVariableNames = ["server_home"]
			});

		Assert.Multiple(() =>
		{
			Assert.That(plugin.Rejections.Single(), Is.EqualTo(new[] { "server_home" }));
			Assert.That(result.RetryOnConflict, Is.True);
		});
	}

	[Test]
	public async Task A_host_that_sends_no_rejected_names_reads_as_a_first_attempt()
	{
		var plugin = new RetryingIntegration();
		var handler = new ConfigEntriesCapabilityHandler([plugin]);

		await PrepareAsync(handler,
			new ConfigEntryPrepareArguments
			{
				EntryId = Guid.NewGuid(), Title = "Home", Values = new Dictionary<string, JsonElement>()
			});

		Assert.That(plugin.Rejections.Single(), Is.Empty);
	}

	[Test]
	public async Task Synchronizing_hands_the_plugin_its_variable_api()
	{
		var plugin = new RetryingIntegration();
		var variables = new RemoteVariableApi(new SilentHost());
		var handler = new ConfigEntriesCapabilityHandler([plugin], new VariablesOnlyContext(variables));

		await handler.InvokeAsync(Invocation(CapabilityOperations.ConfigEntries.Synchronize,
				new ConfigEntrySynchronizeArguments { Entries = [] }),
			CancellationToken.None);

		Assert.That(plugin.SynchronizedWith, Is.EqualTo(new IVariableApi?[] { variables }));
	}

	[Test]
	public void A_flow_completed_without_an_entry_says_so_on_the_wire_and_a_plain_completion_does_not()
	{
		var withoutEntry = ConfigFlowDescriptorMapper.ToDto(ConfigFlowResult.CompleteWithoutEntry());
		var complete = ConfigFlowDescriptorMapper.ToDto(ConfigFlowResult.Complete("Home"));

		Assert.Multiple(() =>
		{
			Assert.That(withoutEntry.Kind, Is.EqualTo(nameof(ConfigFlowResultKind.Complete)));
			Assert.That(withoutEntry.WithoutEntry, Is.True);
			Assert.That(complete.WithoutEntry, Is.Null);
			Assert.That(JsonSerializer.Serialize(complete, PluginProtocolJson.Options),
				Does.Not.Contain("withoutEntry"));
		});
	}

	private static async Task<ConfigEntryPrepareResult> PrepareAsync(
		ConfigEntriesCapabilityHandler handler,
		ConfigEntryPrepareArguments arguments)
	{
		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.ConfigEntries.Prepare, arguments),
			CancellationToken.None);
		return result.Data!.Value.Deserialize<ConfigEntryPrepareResult>(PluginProtocolJson.Options)!;
	}

	private static CapabilityInvocation Invocation(string operation, object arguments)
	{
		return new CapabilityInvocation
		{
			Kind = CapabilityKinds.ConfigEntries,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "c1",
			Services = new ServiceCollection().BuildServiceProvider()
		};
	}

	private sealed class RetryingIntegration : IPluginIntegration, IConfigEntryLifecycle
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public List<IReadOnlyList<string>> Rejections { get; } = [];

		public List<IVariableApi?> SynchronizedWith { get; } = [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
			ConfigEntryPreparation preparation,
			CancellationToken cancellationToken = default)
		{
			Rejections.Add(preparation.RejectedVariableNames);
			return Task.FromResult(ConfigEntryPreparationResult.Accept() with { RetryOnConflict = true });
		}

		public Task SynchronizeEntriesAsync(
			IReadOnlyList<StoredConfigEntry> entries,
			CancellationToken cancellationToken = default)
		{
			SynchronizedWith.Add(null);
			return Task.CompletedTask;
		}

		public Task SynchronizeEntriesAsync(
			IReadOnlyList<StoredConfigEntry> entries,
			IVariableApi variables,
			CancellationToken cancellationToken)
		{
			SynchronizedWith.Add(variables);
			return Task.CompletedTask;
		}
	}

	private sealed class VariablesOnlyContext(IVariableApi variables) : IIntegrationContext
	{
		public IVariableApi Variables => variables;
		public IUserVariableApi UserVariables => throw new NotSupportedException();
		public IIntegrationConfig Config => throw new NotSupportedException();
		public IDeckNavigator Deck => throw new NotSupportedException();
		public IScriptApi Scripts => throw new NotSupportedException();
		public IWidgetApi Widgets => throw new NotSupportedException();
		public IEventPublisher Events => throw new NotSupportedException();
		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class SilentHost : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<JsonElement?>(null);
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
