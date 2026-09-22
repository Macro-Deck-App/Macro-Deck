using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// A plugin registering images over the real wire: the SDK's registry uploads through the asset
/// pipeline and registers through the host's callback router, and the bytes come back out of the same
/// store the resource endpoint serves.
/// </summary>
[TestFixture]
internal sealed class UiResourceContractTests : CapabilityContractFixture
{
	private UiResourceStore _store = null!;
	private PluginUiResources _resources = null!;
	private int _uiResourceCommits;

	[SetUp]
	public void UiResourceSetUp()
	{
		_store = new UiResourceStore();
		_resources = new PluginUiResources(_store, AssetReceiver, SessionRegistry);
		AssetReceiver.AssetCommitted += (_, e) =>
		{
			if (e.Kind == AssetKinds.UiResource)
			{
				Interlocked.Increment(ref _uiResourceCommits);
			}
		};

		var services = new ServiceCollection().BuildServiceProvider();
		var router = new PluginCallbackRouter(SessionRegistry,
			Invoker,
			services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 20, refillPerSecond: 10),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			uiResources: _resources);

		HostInvokeHandler = (correlationId, payload, cancellationToken)
			=> router.RouteAsync(PluginId, SessionId, correlationId, payload, cancellationToken);
	}

	[TearDown]
	public void UiResourceTearDown() => _resources.Dispose();

	[Test]
	public async Task Fifty_images_registered_at_once_are_all_served_with_their_exact_bytes()
	{
		var registry = await ConnectPluginAsync();
		var images = Enumerable.Range(0, 50).Select(index => Image(index, 4096)).ToArray();

		var handles = await Task.WhenAll(images.Select((bytes, index)
			=> registry.RegisterAsync($"photo-{index}", bytes, "image/jpeg")));

		Assert.Multiple(() =>
		{
			for (var index = 0; index < images.Length; index++)
			{
				Assert.That(_store.TryGet(handles[index].ResourceId, out var served), Is.True);
				Assert.That(served.Content.ToArray(), Is.EqualTo(images[index]), $"photo-{index}");
				Assert.That(handles[index].ContentHash, Is.EqualTo(served.ContentHash));
			}
		});
	}

	[Test]
	public async Task Registering_a_name_again_serves_the_new_bytes_and_unchanged_bytes_are_not_resent()
	{
		var registry = await ConnectPluginAsync();
		var first = await registry.RegisterAsync("photo", Image(1, 100), "image/png");
		var second = await registry.RegisterAsync("photo", Image(2, 100), "image/png");
		var commitsBeforeRepeat = _uiResourceCommits;

		var repeat = await registry.RegisterAsync("photo", Image(2, 100), "image/png");

		_store.TryGet(second.ResourceId, out var served);
		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(served.Content.ToArray(), Is.EqualTo(Image(2, 100)));
			Assert.That(repeat, Is.EqualTo(second));
			Assert.That(_uiResourceCommits, Is.EqualTo(commitsBeforeRepeat));
		});
	}

	[Test]
	public async Task A_plugin_over_its_quota_is_told_so()
	{
		var registry = await ConnectPluginAsync();
		var fits = ProtocolLimits.MaxUiResourceBytesPerPlugin / ProtocolLimits.MaxUiResourceBytes;
		for (var index = 0; index < fits; index++)
		{
			await registry.RegisterAsync($"photo-{index}",
				Image(index, ProtocolLimits.MaxUiResourceBytes),
				"image/png");
		}

		var exception = Assert.ThrowsAsync<UiResourceException>(
			() => registry.RegisterAsync("one-more", Image(99, 10), "image/png"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.QuotaExceeded));
	}

	[Test]
	public async Task A_host_without_the_operation_is_reported_unsupported_before_any_bytes_are_sent()
	{
		HostInvokeHandler = (_, payload, _) => Task.FromResult(HostCallbackResult.Fail(
			ProtocolErrorCodes.CapabilityUnsupported,
			$"The '{payload.Api}' api has no operation '{payload.Operation}'."));
		var registry = await ConnectPluginAsync();

		var exception = Assert.ThrowsAsync<UiResourceException>(
			() => registry.RegisterAsync("photo", Image(1, 100), "image/png"));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
			Assert.That(_uiResourceCommits, Is.Zero);
		});
	}

	private async Task<RemoteUiResourceRegistry> ConnectPluginAsync()
	{
		await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(new TestAction("show"))])],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions, LocalId = "show",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.Actions]);

		return new RemoteUiResourceRegistry(CreatePluginHostInvoker(), AssetUploader);
	}

	private static byte[] Image(int seed, int length)
	{
		var bytes = new byte[length];
		new Random(seed).NextBytes(bytes);
		return bytes;
	}
}
