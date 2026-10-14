using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteDesktopNotifierTests
{
	private RemoteIntegrationContextTests.RecordingHostInvoker _invoker = null!;
	private HostStateCache _cache = null!;

	[SetUp]
	public void SetUp()
	{
		_invoker = new RemoteIntegrationContextTests.RecordingHostInvoker();
		_cache = new HostStateCache(new PluginConnectionState());
	}

	[TearDown]
	public void TearDown()
	{
		_invoker.Dispose();
	}

	[Test]
	public async Task On_a_host_that_never_reports_desktop_notifications_they_are_unsupported_and_nothing_is_sent()
	{
		var notifier = new RemoteDesktopNotifier(_invoker, _cache, Serilog.Core.Logger.None);

		await notifier.ShowAsync("Hello", "World");

		Assert.Multiple(() =>
		{
			Assert.That(notifier.IsSupported, Is.False);
			Assert.That(_invoker.LastApi, Is.Null);
		});
	}

	[Test]
	public async Task Once_the_host_reports_support_a_notification_goes_to_the_desktop_notifications_api()
	{
		var notifier = new RemoteDesktopNotifier(_invoker, _cache, Serilog.Core.Logger.None);
		Push(true);

		await notifier.ShowAsync("Build finished", "All green");

		Assert.Multiple(() =>
		{
			Assert.That(notifier.IsSupported, Is.True);
			Assert.That((_invoker.LastApi, _invoker.LastOperation), Is.EqualTo(("desktop-notifications", "show")));
			Assert.That(_invoker.LastArguments,
				Is.EqualTo(new DesktopNotificationShowArguments { Title = "Build finished", Message = "All green" }));
		});
	}

	[Test]
	public async Task A_host_that_reports_no_support_is_not_asked()
	{
		var notifier = new RemoteDesktopNotifier(_invoker, _cache, Serilog.Core.Logger.None);
		Push(false);

		await notifier.ShowAsync("Hello", "World");

		Assert.Multiple(() =>
		{
			Assert.That(notifier.IsSupported, Is.False);
			Assert.That(_invoker.LastApi, Is.Null);
		});
	}

	[TestCase(ProtocolErrorCodes.DesktopNotificationsNotAllowed)]
	[TestCase(ProtocolErrorCodes.CapabilityUnsupported)]
	[TestCase(ProtocolErrorCodes.Timeout)]
	public void A_refused_or_failed_notification_completes_without_throwing(string code)
	{
		var notifier = new RemoteDesktopNotifier(
			new FailingHostInvoker(new ProtocolError { Code = code, Message = "no", Retryable = false }),
			_cache,
			Serilog.Core.Logger.None);
		Push(true);

		Assert.DoesNotThrowAsync(() => notifier.ShowAsync("Hello", "World"));
	}

	[Test]
	public void A_cancelled_call_throws()
	{
		var notifier = new RemoteDesktopNotifier(_invoker, _cache, Serilog.Core.Logger.None);
		Push(true);

		Assert.ThrowsAsync<OperationCanceledException>(() =>
			notifier.ShowAsync("Hello", "World", new CancellationToken(true)));
	}

	private void Push(bool supported)
	{
		_cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.DesktopNotifications,
					Data = JsonSerializer.SerializeToElement(new DesktopNotificationsStateDto { Supported = supported },
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});
	}

	private sealed class FailingHostInvoker(ProtocolError error) : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			return Task.FromException<JsonElement?>(HostInvocationException.From(error));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
