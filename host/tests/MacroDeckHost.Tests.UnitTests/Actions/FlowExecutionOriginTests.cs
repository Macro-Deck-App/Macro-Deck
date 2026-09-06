using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Tests.UnitTests.Actions;

/// <summary>
/// The one place a device origin can enter the execution pipeline. Every ingress that carries a
/// caller-supplied client id - the actions and scripts HTTP endpoints, the UI WebSocket handlers, a
/// plugin callback - ends up building one of these, so the guard is asserted here rather than once per
/// endpoint: a device origin routes navigation to a device session, and honouring a supplied one would
/// hand any authenticated caller a session it does not own.
/// </summary>
[TestFixture]
public class FlowExecutionOriginTests
{
	[Test]
	public void A_caller_supplied_device_origin_is_dropped()
	{
		var request = new FlowExecutionRequest
		{
			Trigger = TriggerSelector.ByType(WidgetTriggerTypes.ShortPress),
			OriginClientId = DeviceOrigin.For(Guid.NewGuid())
		};

		Assert.That(request.OriginClientId, Is.Null);
	}

	[Test]
	public void A_real_client_id_is_kept()
	{
		var request = new FlowExecutionRequest
		{
			Trigger = TriggerSelector.ByType(WidgetTriggerTypes.ShortPress), OriginClientId = "client-1"
		};

		Assert.That(request.OriginClientId, Is.EqualTo("client-1"));
	}

	[Test]
	public void Only_the_hosts_own_device_id_mints_a_device_origin()
	{
		var deviceId = Guid.NewGuid();

		var request = new FlowExecutionRequest
		{
			Trigger = TriggerSelector.ByType(WidgetTriggerTypes.ShortPress),
			OriginClientId = "client-1",
			OriginDeviceId = deviceId
		};

		Assert.Multiple(() =>
		{
			Assert.That(request.OriginClientId, Is.EqualTo(DeviceOrigin.For(deviceId)));
			Assert.That(DeviceOrigin.TryParse(request.OriginClientId, out var parsed) ? parsed : Guid.Empty,
				Is.EqualTo(deviceId));
		});
	}
}
