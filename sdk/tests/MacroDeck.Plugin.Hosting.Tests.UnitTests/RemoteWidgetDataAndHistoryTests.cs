using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteWidgetDataAndHistoryTests
{
	[Test]
	public async Task A_widgets_stored_data_arrives_as_the_host_answered_it()
	{
		var host = new ScriptedHost
		{
			Answer = (_, _) => JsonSerializer.SerializeToElement(new WidgetDataDto
				{
					WidgetId = "w1",
					WidgetTypeId = "com.example::chat",
					Data = JsonSerializer.SerializeToElement(new { allowModeration = false })
				},
				PluginProtocolJson.Options)
		};

		var data = await new RemoteWidgetApi(host,
				new PluginConnectionState(),
				new HostStateCache(new PluginConnectionState()))
			.GetWidgetDataAsync("w1");

		Assert.Multiple(() =>
		{
			Assert.That(host.Calls.Single(), Is.EqualTo((HostApis.Widgets, HostOperations.Widgets.GetData)));
			Assert.That(data?.WidgetTypeId, Is.EqualTo("com.example::chat"));
			Assert.That(data?.Data.GetProperty("allowModeration").GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task A_macro_deck_without_widget_data_answers_none()
	{
		var host = new ScriptedHost { Failure = Unsupported() };

		var data = await new RemoteWidgetApi(host,
				new PluginConnectionState(),
				new HostStateCache(new PluginConnectionState()))
			.GetWidgetDataAsync("w1");

		Assert.That(data, Is.Null);
	}

	[Test]
	public async Task A_history_window_reads_the_hosts_samples_and_closes_its_window_when_disposed()
	{
		var host = new ScriptedHost
		{
			Answer = (_, operation) => operation switch
			{
				HostOperations.Variables.HistoryOpen => JsonSerializer.SerializeToElement(
					new VariablesHistoryOpenResult { WindowId = "window-1" },
					PluginProtocolJson.Options),
				HostOperations.Variables.HistoryRead => JsonSerializer.SerializeToElement(
					new VariablesHistoryReadResult { Values = [1, null, 3] },
					PluginProtocolJson.Options),
				_ => null
			}
		};

		var window = await new RemoteVariableApi(host).OpenHistoryAsync("viewer_count", 40);
		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		window.Changed += (_, _) => changed.TrySetResult();
		if (window.Values.Count == 0)
		{
			await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}

		var values = window.Values;
		await window.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(values, Is.EqualTo(new double?[] { 1, null, 3 }));
			Assert.That(host.Calls[0], Is.EqualTo((HostApis.Variables, HostOperations.Variables.HistoryOpen)));
			Assert.That(host.Calls[^1], Is.EqualTo((HostApis.Variables, HostOperations.Variables.HistoryClose)));
		});
	}

	[Test]
	public void A_macro_deck_without_variable_history_reports_it_as_unsupported()
	{
		var host = new ScriptedHost { Failure = Unsupported() };

		var exception = Assert.ThrowsAsync<VariableHistoryException>(()
			=> new RemoteVariableApi(host).OpenHistoryAsync("viewer_count", 40));

		Assert.That(exception!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.Unsupported));
	}

	[Test]
	public void A_name_that_is_not_the_plugins_own_variable_is_reported_as_not_owned()
	{
		var host = new ScriptedHost
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.InvalidPayload, "not yours")
		};

		var exception = Assert.ThrowsAsync<VariableHistoryException>(()
			=> new RemoteVariableApi(host).OpenHistoryAsync("someone_elses_count", 40));

		Assert.That(exception!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.NotOwned));
	}

	private static HostInvocationException Unsupported()
	{
		return HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no");
	}

	private sealed class ScriptedHost : IHostInvoker
	{
		private readonly Lock _gate = new();
		private readonly List<(string Api, string Operation)> _calls = [];

		public Func<string, string, JsonElement?> Answer { get; init; } = (_, _) => null;

		public Exception? Failure { get; init; }

		public IReadOnlyList<(string Api, string Operation)> Calls
		{
			get
			{
				lock (_gate)
				{
					return [.. _calls];
				}
			}
		}

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_calls.Add((api, operation));
			}

			return Failure is { } failure
				? Task.FromException<JsonElement?>(failure)
				: Task.FromResult(Answer(api, operation));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
