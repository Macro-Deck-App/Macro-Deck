using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

/// <summary>Covers the render-signal seam beside <c>LabelRenderBackgroundService.Process</c>'s client
/// push: an open in-process session must see exactly the resolved text a legacy client is pushed, for
/// the same widget/state, from the same debounced batch.</summary>
[TestFixture]
internal sealed class LabelRenderBackgroundServiceTests
{
	private RecordingTransport _transport = null!;
	private LabelRenderChannel _queue = null!;
	private LabelSubscriptionTracker _subscriptions = null!;
	private RecordingRenderSignals _renderSignals = null!;
	private FakeLabelTextService _labelText = null!;
	private ServiceProvider _serviceProvider = null!;
	private LabelRenderBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_transport = new RecordingTransport();
		_queue = new LabelRenderChannel();
		_subscriptions = new LabelSubscriptionTracker();
		_renderSignals = new RecordingRenderSignals();
		_labelText = new FakeLabelTextService();
		_serviceProvider = new ServiceCollection()
			.AddScoped<ILabelTextService>(_ => _labelText)
			.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		_service = new LabelRenderBackgroundService(new StartedHostLifetime(),
			_queue,
			_serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			_transport,
			_subscriptions,
			_renderSignals,
			Serilog.Log.Logger);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
		await _serviceProvider.DisposeAsync();
	}

	[Test]
	public async Task Process_RaisesTheRenderSignal_WithTheExactTextPushedToClients()
	{
		var widgetId = Guid.NewGuid();
		_subscriptions.Add("conn-1", widgetId.ToString(), "on");
		_labelText.TextByState[(widgetId, "on")] = "On label";

		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(widgetId);

		await WaitUntil(() => _transport.Sent.Count == 1 && _renderSignals.LabelChanges.Count == 1);

		var pushed = _transport.Sent[0];
		var raised = _renderSignals.LabelChanges[0];
		Assert.Multiple(() =>
		{
			Assert.That(raised.WidgetId, Is.EqualTo(pushed.WidgetId));
			Assert.That(raised.State, Is.EqualTo(pushed.State));
			Assert.That(raised.Text, Is.EqualTo(pushed.Text));
			Assert.That(raised.Text, Is.EqualTo("On label"), "sanity: the resolved text actually reached both sides");
		});
	}

	[Test]
	public async Task Process_pushes_the_extra_label_texts_with_the_primary_one_and_none_for_a_button_without_them()
	{
		var withExtras = Guid.NewGuid();
		var plain = Guid.NewGuid();
		_subscriptions.Add("conn-1", withExtras.ToString(), "on");
		_subscriptions.Add("conn-1", plain.ToString(), "on");
		_labelText.TextByState[(withExtras, "on")] = "Main";
		_labelText.ExtraTextsByState[(withExtras, "on")] = ["42 %", null];
		_labelText.TextByState[(plain, "on")] = "Plain";

		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(withExtras);
		_queue.Enqueue(plain);

		await WaitUntil(() => _transport.Sent.Count == 2 && _renderSignals.LabelChanges.Count == 2);

		var rich = _transport.Sent.Single(evt => evt.WidgetId == withExtras.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(rich.Text, Is.EqualTo("Main"));
			Assert.That(rich.ExtraTexts, Is.EqualTo(new[] { "42 %", null }));
			Assert.That(_renderSignals.LabelChanges.Single(evt => evt.WidgetId == withExtras.ToString()).ExtraTexts,
				Is.EqualTo(new[] { "42 %", null }));
			Assert.That(_transport.Sent.Single(evt => evt.WidgetId == plain.ToString()).ExtraTexts, Is.Null);
		});
	}

	// A widget nobody is subscribed to must neither be pushed to clients nor raise the render signal -
	// Process returns before it ever resolves or raises anything for it.
	[Test]
	public async Task Process_RaisesNeitherPushNorRenderSignal_ForAWidgetWithNoSubscribers()
	{
		var widgetId = Guid.NewGuid();
		var otherWidgetId = Guid.NewGuid();
		_subscriptions.Add("conn-1", otherWidgetId.ToString(), "on");
		_labelText.TextByState[(widgetId, "on")] = "Should never be resolved";

		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(widgetId);
		_queue.Enqueue(otherWidgetId);

		await WaitUntil(() => _transport.Sent.Count >= 1 && _renderSignals.LabelChanges.Count >= 1);

		Assert.Multiple(() =>
		{
			Assert.That(_transport.Sent.Any(e => e.WidgetId == widgetId.ToString()), Is.False);
			Assert.That(_renderSignals.LabelChanges.Any(e => e.WidgetId == widgetId.ToString()), Is.False);
		});
	}

	[Test]
	public async Task A_minute_render_pushes_a_label_only_when_its_text_changed()
	{
		var unchanged = Guid.NewGuid();
		var changed = Guid.NewGuid();
		_subscriptions.Add("conn-1", unchanged.ToString(), "on");
		_subscriptions.Add("conn-1", changed.ToString(), "on");
		_labelText.TextByState[(unchanged, "on")] = "Mon";
		_labelText.TextByState[(changed, "on")] = "12:00";
		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(unchanged);
		_queue.Enqueue(changed);
		await WaitUntil(() => _transport.Sent.Count == 2);

		_labelText.TextByState[(changed, "on")] = "12:01";
		_queue.EnqueueIfChanged(unchanged);
		_queue.EnqueueIfChanged(changed);
		await WaitUntil(() => _transport.Sent.Count >= 3);
		_queue.Enqueue(unchanged);
		await WaitUntil(() => _transport.Sent.Count >= 4);

		Assert.That(_transport.Sent.Select(sent => sent.Text), Is.EqualTo(new[] { "Mon", "12:00", "12:01", "Mon" }));
	}

	[Test]
	public async Task A_minute_render_pushes_when_only_an_extra_label_changed()
	{
		var widgetId = Guid.NewGuid();
		_subscriptions.Add("conn-1", widgetId.ToString(), "on");
		_labelText.TextByState[(widgetId, "on")] = "Main";
		_labelText.ExtraTextsByState[(widgetId, "on")] = ["12:00"];
		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(widgetId);
		await WaitUntil(() => _transport.Sent.Count == 1);

		_labelText.ExtraTextsByState[(widgetId, "on")] = ["12:01"];
		_queue.EnqueueIfChanged(widgetId);
		await WaitUntil(() => _transport.Sent.Count == 2);

		Assert.That(_transport.Sent[1].ExtraTexts, Is.EqualTo(new[] { "12:01" }));
	}

	[Test]
	public async Task A_minute_render_pushes_to_a_viewer_that_joined_after_everyone_left()
	{
		var widgetId = Guid.NewGuid();
		_subscriptions.Add("conn-1", widgetId.ToString(), "on");
		_labelText.TextByState[(widgetId, "on")] = "Mon";
		await _service.StartAsync(CancellationToken.None);
		_queue.Enqueue(widgetId);
		await WaitUntil(() => _transport.Sent.Count == 1);

		_subscriptions.RemoveConnection("conn-1");
		_subscriptions.Add("conn-2", widgetId.ToString(), "on");
		_queue.EnqueueIfChanged(widgetId);
		await WaitUntil(() => _transport.Sent.Count == 2);

		Assert.That(_transport.Sent[1].Text, Is.EqualTo("Mon"));
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("Timed out waiting for condition.");
			}

			await Task.Delay(25);
		}
	}

	private sealed class FakeLabelTextService : ILabelTextService
	{
		public Dictionary<(Guid WidgetId, string State), string> TextByState { get; } = [];

		public Dictionary<(Guid WidgetId, string State), IReadOnlyList<string?>> ExtraTextsByState { get; } = [];

		public Task<LabelTexts> ResolveTexts(
			Guid widgetId,
			string state,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new LabelTexts(TextByState.GetValueOrDefault((widgetId, state)),
				ExtraTextsByState.GetValueOrDefault((widgetId, state)) ?? []));
		}

		public Task<string?> ResolvePreview(
			LabelImagePreviewRequest request,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class RecordingTransport : IUiTransport
	{
		public List<LabelTextUpdatedEvent> Sent { get; } = [];

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Task.CompletedTask;
		}

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			if (message is LabelTextUpdatedEvent evt)
			{
				Sent.Add(evt);
			}

			return Task.CompletedTask;
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
