using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// The host's own record of what a plugin is watching must clamp to
/// <see cref="ProtocolLimits.MaxVariableSubscriptions"/> the same way the plugin-side SDK's
/// <c>VariableSubscriptions</c> does - otherwise the two disagree about what is actually subscribed,
/// and past the limit the excess ids are dropped with no diagnostic anywhere (issue #760 review finding 7).
/// </summary>
[TestFixture]
internal sealed class RemoteVariableSubscriptionsTests
{
	private const string PluginId = "com.example.smart-home";

	[Test]
	public void A_working_set_within_the_limit_is_recorded_in_full()
	{
		var subscriptions = new RemoteVariableSubscriptions(Serilog.Log.Logger);
		var ids = Enumerable.Range(0, 10).Select(i => $"entity/{i}").ToList();

		subscriptions.Replace(PluginId, ids);

		Assert.Multiple(() =>
		{
			foreach (var id in ids)
			{
				Assert.That(subscriptions.IsSubscribed(PluginId, id), Is.True);
			}
		});
	}

	[Test]
	public void A_working_set_past_the_limit_is_clamped_not_recorded_in_full()
	{
		var subscriptions = new RemoteVariableSubscriptions(Serilog.Log.Logger);
		var ids = Enumerable.Range(0, ProtocolLimits.MaxVariableSubscriptions + 50)
			.Select(i => $"entity/{i}")
			.ToList();

		subscriptions.Replace(PluginId, ids);

		var subscribedCount = ids.Count(id => subscriptions.IsSubscribed(PluginId, id));

		Assert.That(subscribedCount,
			Is.EqualTo(ProtocolLimits.MaxVariableSubscriptions),
			"the host's own bookkeeping must clamp to the same limit the plugin-side SDK enforces, or the " +
			"two sides disagree about what is actually subscribed");
	}
}
