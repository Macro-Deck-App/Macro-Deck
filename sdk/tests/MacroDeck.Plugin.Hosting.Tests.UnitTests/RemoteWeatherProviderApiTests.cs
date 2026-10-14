using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteWeatherProviderApiTests
{
	[Test]
	public async Task A_notice_reaches_the_host_as_weather_snapshots_changed()
	{
		var host = new GatedHost();
		host.Release();

		await new RemoteWeatherProviderApi(host).NotifySnapshotsChangedAsync();

		Assert.That(host.Calls, Is.EqualTo(new[] { (HostApis.Weather, HostOperations.Weather.SnapshotsChanged) }));
	}

	[Test]
	public async Task Notices_made_while_one_is_on_its_way_are_merged_into_one_more()
	{
		var host = new GatedHost();
		var api = new RemoteWeatherProviderApi(host);

		var first = api.NotifySnapshotsChangedAsync();
		await host.WaitForCallsAsync(1);
		var merged = Enumerable.Range(0, 10).Select(_ => api.NotifySnapshotsChangedAsync()).ToArray();
		host.Release();
		await Task.WhenAll([first, .. merged]).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(host.Calls, Has.Count.EqualTo(2));
	}

	[TestCase(ProtocolErrorCodes.CapabilityUnsupported)]
	[TestCase(ProtocolErrorCodes.CapabilityUnavailable)]
	public void A_host_that_refuses_the_notice_makes_it_a_no_op(string code)
	{
		var host = new GatedHost { Failure = HostInvocationException.CreateNonRetryable(code, "no") };
		host.Release();

		Assert.DoesNotThrowAsync(() => new RemoteWeatherProviderApi(host).NotifySnapshotsChangedAsync());
	}

	private sealed class GatedHost : IHostInvoker
	{
		private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly Lock _gate = new();
		private readonly List<(string, string)> _calls = [];

		public Exception? Failure { get; init; }

		public IReadOnlyList<(string, string)> Calls
		{
			get
			{
				lock (_gate)
				{
					return [.. _calls];
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
				_calls.Add((api, operation));
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
			for (var attempt = 0; attempt < 500 && Calls.Count < count; attempt++)
			{
				await Task.Delay(10);
			}
		}
	}
}
