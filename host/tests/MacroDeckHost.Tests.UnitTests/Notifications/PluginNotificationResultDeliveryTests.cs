using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class PluginNotificationResultDeliveryTests
{
	private static readonly NotificationOwner _plugin =
		new(NotificationSourceCategory.Plugin, "com.example.plugin", "Example Plugin");

	private PluginSessionRegistry _sessions = null!;
	private NotificationManager _manager = null!;
	private RecordingInvoker _invoker = null!;
	private PluginNotificationResultDelivery _delivery = null!;

	[SetUp]
	public async Task SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_manager = new NotificationManager(new UserNotificationStore(),
			TestLocalization.Resolver,
			TestLocalization.ScopeFactory,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		_invoker = new RecordingInvoker();
		_delivery = new PluginNotificationResultDelivery(_manager,
			_sessions,
			_invoker,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			TimeSpan.FromMilliseconds(2));

		await _delivery.StartAsync(CancellationToken.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _delivery.StopAsync(CancellationToken.None);
		_delivery.Dispose();
		_manager.Dispose();
	}

	[Test]
	public async Task A_plugin_is_told_how_its_notification_ended()
	{
		var shown = await ShowDialog(_plugin);

		_manager.Respond(new NotificationPrincipal(true, null),
			shown.Id,
			"yes",
			new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") });

		Assert.That(() => _invoker.Delivered.Count, Is.EqualTo(1).After(2000, 10));
		var (pluginId, arguments) = _invoker.Delivered.Single();
		Assert.Multiple(() =>
		{
			Assert.That(pluginId, Is.EqualTo(_plugin.Id));
			Assert.That(arguments.NotificationId, Is.EqualTo(shown.Id));
			Assert.That(arguments.CloseReason, Is.EqualTo(NotificationsWire.CloseReason.Action));
			Assert.That(arguments.ActionId, Is.EqualTo("yes"));
			Assert.That(arguments.ClientId, Is.EqualTo("admin"));
		});
	}

	[Test]
	public async Task A_result_the_plugin_cannot_take_yet_is_kept_and_sent_when_it_can()
	{
		_invoker.Accepting = false;
		var shown = await ShowDialog(_plugin);

		_manager.Respond(new NotificationPrincipal(true, null), shown.Id, "yes", null);
		await Task.Delay(100);
		var deliveredWhileDetached = _invoker.Delivered.Count;
		_invoker.Accepting = true;
		await _sessions.Create(Session("session-1"));
		_sessions.TryAttach("session-1", new FakePluginConnection(), null);

		Assert.That(() => _invoker.Delivered.Count, Is.EqualTo(1).After(2000, 10));
		Assert.That(deliveredWhileDetached, Is.Zero);
		Assert.That(_invoker.Attempts, Is.GreaterThan(1));
	}

	[Test]
	public async Task Retrying_many_times_does_not_use_up_a_result_that_is_still_within_the_resume_window()
	{
		_invoker.Accepting = false;
		var shown = await ShowDialog(_plugin);
		_manager.Respond(new NotificationPrincipal(true, null), shown.Id, "yes", null);

		Assert.That(() => _invoker.Attempts, Is.GreaterThan(60).After(3000, 10));
		_invoker.Accepting = true;

		Assert.That(() => _invoker.Delivered.Count, Is.EqualTo(1).After(2000, 10));
	}

	[Test]
	public async Task A_result_still_queued_when_the_session_is_pruned_is_discarded()
	{
		await _sessions.Create(Session("session-1"));
		_invoker.Accepting = false;
		var shown = await ShowDialog(_plugin);
		_manager.Respond(new NotificationPrincipal(true, null), shown.Id, "yes", null);
		await Task.Delay(100);

		await _sessions.Create(Session("session-2"));
		_invoker.Accepting = true;
		await Task.Delay(200);

		Assert.That(_invoker.Delivered, Is.Empty);
	}

	[Test]
	public async Task An_inbox_only_notification_is_reported_complete_to_its_plugin()
	{
		var shown = await _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Saved", Popup = NotificationPopup.None, Inbox = true });

		Assert.That(() => _invoker.Delivered.Count, Is.EqualTo(1).After(2000, 10));
		Assert.Multiple(() =>
		{
			Assert.That(_invoker.Delivered.Single().Arguments.NotificationId, Is.EqualTo(shown.Id));
			Assert.That(_invoker.Delivered.Single().Arguments.CloseReason,
				Is.EqualTo(NotificationsWire.CloseReason.Programmatic));
		});
	}

	[Test]
	public async Task An_owner_that_is_gone_is_not_told()
	{
		var shown = await ShowDialog(_plugin);

		_manager.CloseOwner(_plugin.Id);
		await Task.Delay(150);

		Assert.That(shown.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.OwnerGone));
		Assert.That(_invoker.Delivered, Is.Empty);
	}

	[Test]
	public async Task Only_plugins_are_told()
	{
		var builtIn = new NotificationOwner(NotificationSourceCategory.BuiltInIntegration, "system", "System");
		var shown = await ShowDialog(builtIn);

		_manager.Respond(new NotificationPrincipal(true, null), shown.Id, "yes", null);
		await Task.Delay(150);

		Assert.That(_invoker.Delivered, Is.Empty);
	}

	private Task<NotificationShowOutcome> ShowDialog(NotificationOwner owner)
	{
		return _manager.ShowAsync(owner,
			new NotificationOptions
			{
				Title = "Question",
				Popup = NotificationPopup.Dialog,
				Actions = [new NotificationAction { Id = "yes", Label = "Yes" }]
			});
	}

	private static PluginSessionRecord Session(string sessionId)
	{
		return new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = _plugin.Id,
			DisplayName = _plugin.Name,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = DateTimeOffset.UtcNow
		};
	}

	private sealed class RecordingInvoker : IPluginCapabilityInvoker
	{
		private readonly Lock _gate = new();
		private readonly List<(string PluginId, UiNotificationResultArguments Arguments)> _delivered = [];
		private int _attempts;

		public bool Accepting { get; set; } = true;

		public int Attempts => Volatile.Read(ref _attempts);

		public IReadOnlyList<(string PluginId, UiNotificationResultArguments Arguments)> Delivered
		{
			get
			{
				lock (_gate)
				{
					return [.. _delivered];
				}
			}
		}

		public Task<JsonElement?> InvokeAsync(
			string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _attempts);

			if (!Accepting)
			{
				return Task.FromException<JsonElement?>(RemoteCapabilityException.CreateRetryable(
					ProtocolErrorCodes.CapabilityUnavailable,
					"The plugin has no attached connection."));
			}

			Assert.That(request.Kind, Is.EqualTo(CapabilityKinds.Ui));
			Assert.That(request.Operation, Is.EqualTo(CapabilityOperations.Ui.NotificationResult));
			var arguments
				= ((JsonElement)request.Arguments!).Deserialize<UiNotificationResultArguments>(PluginProtocolJson
					.Options)!;
			lock (_gate)
			{
				_delivered.Add((pluginId, arguments));
			}

			return Task.FromResult<JsonElement?>(null);
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result)
		{
			return false;
		}

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId)
		{
			return false;
		}
	}
}
