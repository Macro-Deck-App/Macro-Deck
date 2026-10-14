using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Http;
using MacroDeck.Sdk.Locale;
using MacroDeck.Sdk.Storage;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteLocaleHttpAndStorageTests
{
	[Test]
	public async Task The_time_format_is_the_culture_and_hour_cycle_the_host_answers()
	{
		var host = new AnsweringHost(new LocaleTimeOfDayFormatResult { Culture = "de-DE", HourCycle = HourCycles.H12 });

		var format = await new RemoteLocaleApi(host).GetTimeOfDayFormatAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.Calls, Is.EqualTo(new[] { (HostApis.Locale, HostOperations.Locale.TimeOfDayFormat) }));
			Assert.That(format.Culture.Name, Is.EqualTo("de-DE"));
			Assert.That(format.HourCycle, Is.EqualTo(HourCycles.H12));
		});
	}

	[Test]
	public async Task A_macro_deck_without_the_locale_api_gives_the_current_culture_and_a_24_hour_clock()
	{
		var host = new AnsweringHost(null)
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};

		var format = await new RemoteLocaleApi(host).GetTimeOfDayFormatAsync();

		Assert.Multiple(() =>
		{
			Assert.That(format.Culture, Is.EqualTo(CultureInfo.CurrentCulture));
			Assert.That(format.HourCycle, Is.EqualTo(HourCycles.H23));
		});
	}

	[Test]
	public async Task An_unknown_hour_cycle_formats_as_a_24_hour_clock()
	{
		var host = new AnsweringHost(new LocaleTimeOfDayFormatResult { Culture = "en-US", HourCycle = "h11" });

		var format = await new RemoteLocaleApi(host).GetTimeOfDayFormatAsync();

		Assert.That(format.HourCycle, Is.EqualTo(HourCycles.H23));
	}

	[Test]
	public void The_user_agent_is_the_sdk_default_until_the_host_pushes_one_and_then_follows_every_push()
	{
		var cache = new HostStateCache(new PluginConnectionState());
		var http = new RemoteHostHttp(cache);
		var before = http.UserAgent;

		cache.Apply(StatePush(HostApis.Http, new HttpStateDto { UserAgent = "Custom/1.0" }));
		var first = http.UserAgent;
		cache.Apply(StatePush(HostApis.Http, new HttpStateDto { UserAgent = "Custom/2.0" }));

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EqualTo(DefaultHostHttp.Instance.UserAgent).And.StartsWith("MacroDeck/"));
			Assert.That(first, Is.EqualTo("Custom/1.0"));
			Assert.That(http.UserAgent, Is.EqualTo("Custom/2.0"));
		});
	}

	[Test]
	public async Task Storage_lives_under_the_plugins_own_folder_in_its_data_directory()
	{
		var dataDirectory = Path.Combine(Path.GetTempPath(), "md-storage-" + Guid.NewGuid().ToString("N"));
		try
		{
			var storage = PluginIntegrationStorage.Create(dataDirectory, "com.example.storage");

			await storage.WriteAsync("state.json", "{\"a\":1}");
			var read = await storage.ReadAsync("state.json");

			Assert.Multiple(() =>
			{
				Assert.That(read.Content, Is.EqualTo("{\"a\":1}"));
				Assert.That(File.Exists(Path.Combine(dataDirectory,
						"integration-data",
						"com.example.storage",
						"state.json")),
					Is.True);
			});
		}
		finally
		{
			if (Directory.Exists(dataDirectory))
			{
				Directory.Delete(dataDirectory, true);
			}
		}
	}

	[Test]
	public void A_plugin_without_a_data_directory_reads_nothing_and_cannot_write()
	{
		var storage = PluginIntegrationStorage.Create(null, "com.example.storage");

		Assert.Multiple(async () =>
		{
			Assert.That((await storage.ReadAsync("state.json")).Status,
				Is.EqualTo(IntegrationStorageReadStatus.Missing));
			Assert.ThrowsAsync<InvalidOperationException>(() => storage.WriteAsync("state.json", "{}"));
			Assert.DoesNotThrowAsync(() => storage.DeleteAsync("state.json"));
		});
	}

	private static ProtocolEnvelope StatePush(string api, object? data)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = api,
					Data = data is null ? null : JsonSerializer.SerializeToElement(data, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};
	}

	private sealed class AnsweringHost(object? answer) : IHostInvoker
	{
		public List<(string Api, string Operation)> Calls { get; } = [];

		public Exception? Failure { get; init; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Calls.Add((api, operation));
			if (Failure is { } failure)
			{
				return Task.FromException<JsonElement?>(failure);
			}

			return Task.FromResult<JsonElement?>(answer is null
				? null
				: JsonSerializer.SerializeToElement(answer, PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
