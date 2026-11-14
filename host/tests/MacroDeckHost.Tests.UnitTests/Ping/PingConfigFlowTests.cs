using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Ping.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Ping;

[TestFixture]
internal sealed class PingConfigFlowTests
{
	[TestCase("192.168.1.10", "192.168.1.10")]
	[TestCase(" nas.local ", "nas.local")]
	[TestCase("[fd00::10]", "fd00::10")]
	[TestCase("fe80::1%en0", "fe80::1%en0")]
	public async Task A_valid_target_is_stored_with_its_normalised_host(string entered, string stored)
	{
		var result = await SubmitAsync(Input(entered));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("NAS"));
			Assert.That(result.Values![PingConfigKeys.Host], Is.EqualTo(ConfigFlowValue.Plain(stored)));
		});
	}

	[TestCase("")]
	[TestCase("not a host")]
	[TestCase("http://nas.local")]
	public async Task An_invalid_host_is_rejected_on_its_field(string host)
	{
		var result = await SubmitAsync(Input(host));

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { PingConfigKeys.Host }));
	}

	[Test]
	public async Task Out_of_range_numbers_are_rejected_on_their_fields()
	{
		var result = await SubmitAsync(Input(interval: 0, timeout: 50, threshold: 21));

		Assert.That(result.FieldErrors!.Keys,
			Is.EquivalentTo(new[]
			{
				PingConfigKeys.IntervalSeconds, PingConfigKeys.TimeoutMilliseconds, PingConfigKeys.FailureThreshold
			}));
	}

	[Test]
	public async Task A_timeout_that_is_not_shorter_than_the_interval_is_rejected()
	{
		var result = await SubmitAsync(Input(interval: 2, timeout: 2000));

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { PingConfigKeys.TimeoutMilliseconds }));
	}

	[Test]
	public async Task Left_out_numbers_fall_back_to_the_defaults()
	{
		var result = await SubmitAsync(new Dictionary<string, object?>
		{
			[PingConfigKeys.Name] = "NAS", [PingConfigKeys.Host] = "nas.local"
		});

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
	}

	private static Dictionary<string, object?> Input(
		string host = "nas.local",
		double interval = 10,
		double timeout = 1000,
		double threshold = 3)
	{
		return new Dictionary<string, object?>
		{
			[PingConfigKeys.Name] = "NAS",
			[PingConfigKeys.Host] = host,
			[PingConfigKeys.IntervalSeconds] = interval,
			[PingConfigKeys.TimeoutMilliseconds] = timeout,
			[PingConfigKeys.FailureThreshold] = threshold
		};
	}

	private static Task<ConfigFlowResult> SubmitAsync(IReadOnlyDictionary<string, object?> input)
	{
		return new PingConfigFlow().SubmitAsync("target", input, new FlowContext(), CancellationToken.None);
	}

	private sealed class FlowContext : IConfigFlowContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();
	}
}
