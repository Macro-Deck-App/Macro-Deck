using System.Collections.Concurrent;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class DeviceKeyImageContractTests : CapabilityContractFixture
{
	private const string ProviderDeviceId = "SERIAL-1";

	private static readonly LayoutKeyImageFormat _jpeg = new()
	{
		Encoding = KeyImageEncodings.Jpeg, Rotation = 180, MirrorHorizontal = true, CornerRadius = 10
	};

	private RecordingDeviceSurfaceService _surfaces = null!;
	private RemoteDeviceSessionRegistry _sessionOwners = null!;
	private RemoteDeviceProviderRegistry? _providers;
	private PaintingDeviceProvider _provider = null!;
	private Guid _deviceId;

	[SetUp]
	public void KeyImageSetUp()
	{
		_deviceId = Guid.NewGuid();
		_surfaces = new RecordingDeviceSurfaceService { KeyImageCells = 15 };
		_sessionOwners = new RemoteDeviceSessionRegistry();
		_provider = new PaintingDeviceProvider();

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
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			_surfaces,
			_sessionOwners,
			HostAssetSender);

		HostInvokeHandler = (_, payload, cancellationToken)
			=> router.RouteAsync(PluginId, Guid.NewGuid().ToString(), payload, cancellationToken);
	}

	[TearDown]
	public void KeyImageTearDown()
	{
		_providers?.Dispose();
		_providers = null;
	}

	[Test]
	public async Task A_plugin_that_negotiated_version_two_is_never_sent_the_format_or_a_key_image_push()
	{
		var surfaceProvider = await ConnectProviderAsync(2, 2);

		var accepted = await surfaceProvider.OpenAsync(Descriptor(), CancellationToken.None);
		await surfaceProvider.PushKeyImagesAsync(_deviceId.ToString(), [Image(0, 0, 1, 16)], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.True);
			Assert.That(surfaceProvider.SupportsKeyImages, Is.False);
			Assert.That(_provider.Session!.KeyImageFormat, Is.Null);
			Assert.That(_provider.Painted, Is.Empty);
			Assert.That(FramesFor(CapabilityOperations.DeviceProvider.SessionKeyImages), Is.Zero);
			Assert.That(OpenFrame(), Does.Not.Contain("keyImages"));
		});
	}

	[Test]
	public async Task A_version_three_plugin_is_told_the_format_and_gets_every_picture_inline_or_by_transfer()
	{
		var surfaceProvider = await ConnectProviderAsync(3, 3);
		await surfaceProvider.OpenAsync(Descriptor(), CancellationToken.None);
		var small = Image(0, 0, 1, 1024);
		var large = Image(0, 1, 1, RemoteDeviceSurfaceProvider.MaxInlineKeyImageBytes + 1);

		await surfaceProvider.PushKeyImagesAsync(_deviceId.ToString(), [small, large], CancellationToken.None);
		await WaitForAsync(() => _provider.Painted.Count == 2, "Both pictures should have reached the provider.");

		var painted = _provider.Painted.OrderBy(image => image.Column).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(surfaceProvider.SupportsKeyImages, Is.True);
			Assert.That(_provider.Session!.KeyImageFormat, Is.EqualTo(_jpeg));
			Assert.That(painted[0].Content.ToArray(), Is.EqualTo(small.Content.ToArray()));
			Assert.That(painted[1].Content.ToArray(), Is.EqualTo(large.Content.ToArray()));
			Assert.That(painted.Select(image => image.ContentType), Is.All.EqualTo(KeyImageContentTypes.Jpeg));
			Assert.That(FramesFor(CapabilityOperations.DeviceProvider.SessionKeyImages), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Pictures_are_batched_up_to_the_inline_budget_per_push()
	{
		var surfaceProvider = await ConnectProviderAsync(3, 3);
		await surfaceProvider.OpenAsync(Descriptor(), CancellationToken.None);
		var third = RemoteDeviceSurfaceProvider.MaxInlineKeyImageBytes / 3;

		await surfaceProvider.PushKeyImagesAsync(_deviceId.ToString(),
			[Image(0, 0, 1, third), Image(0, 1, 1, third), Image(0, 2, 1, third), Image(1, 0, 1, third)],
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Painted, Has.Count.EqualTo(4));
			Assert.That(FramesFor(CapabilityOperations.DeviceProvider.SessionKeyImages), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_cell_is_fetched_from_the_host_inline_or_by_transfer_and_an_empty_answer_is_null()
	{
		var surfaceProvider = await ConnectProviderAsync(3, 3);
		await surfaceProvider.OpenAsync(Descriptor(), CancellationToken.None);
		var small = Image(1, 2, 4, 2048);
		var large = Image(2, 0, 7, RemoteDeviceSurfaceProvider.MaxInlineKeyImageBytes * 2);

		_surfaces.NextKeyImage = small;
		var inline = await _provider.Session!.GetKeyImageAsync(1, 2);
		_surfaces.NextKeyImage = large;
		var transferred = await _provider.Session.GetKeyImageAsync(2, 0);
		_surfaces.NextKeyImage = null;
		var none = await _provider.Session.GetKeyImageAsync(0, 0);

		Assert.Multiple(() =>
		{
			Assert.That((inline!.Row, inline.Column, inline.Revision), Is.EqualTo((1, 2, 4L)));
			Assert.That(inline.Content.ToArray(), Is.EqualTo(small.Content.ToArray()));
			Assert.That(transferred!.Revision, Is.EqualTo(7));
			Assert.That(transferred.Content.ToArray(), Is.EqualTo(large.Content.ToArray()));
			Assert.That(none, Is.Null);
			Assert.That(_surfaces.KeyImageRequests,
				Is.EqualTo(new[] { (_deviceId, 1, 2), (_deviceId, 2, 0), (_deviceId, 0, 0) }));
		});
	}

	[Test]
	public async Task A_plugin_that_asks_for_its_keys_while_it_opens_the_session_gets_them_and_the_open_succeeds()
	{
		var surfaceProvider = await ConnectProviderAsync(3, 3);
		var small = Image(0, 0, 1, 2048);
		var large = Image(0, 1, 1, RemoteDeviceSurfaceProvider.MaxInlineKeyImageBytes + 1);
		_surfaces.KeyImageAnswer = (_, _, column) => column == 0 ? small : large;
		_provider.FetchOnOpen = [(0, 0), (0, 1)];

		var accepted = await surfaceProvider.OpenAsync(Descriptor(), CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.True);
			Assert.That(_provider.Fetched.Select(image => image?.Content.ToArray()),
				Is.EqualTo(new[] { small.Content.ToArray(), large.Content.ToArray() }));
		});
	}

	private async Task<IDeviceSurfaceProvider> ConnectProviderAsync(int declaredMaximum, int negotiatedVersion)
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([_provider],
					TestMetadata.Default,
					CreatePluginHostInvoker(),
					PluginHostAssets)
			],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.DeviceProvider,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = declaredMaximum }
				}
			],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: negotiatedVersion);

		_providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, _sessionOwners, HostAssetSender);
		return _providers.Resolve(PluginId)!;
	}

	private DeviceSurfaceSessionDescriptor Descriptor()
	{
		return new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId, _jpeg);
	}

	private static DeviceKeyImage Image(int row, int column, long revision, int length)
	{
		var content = new byte[length];
		Random.Shared.NextBytes(content);
		return new DeviceKeyImage
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Jpeg,
			Content = content
		};
	}

	private int FramesFor(string operation)
	{
		return SentInvokes().Count(raw => raw.Contains($"\"{operation}\"", StringComparison.Ordinal));
	}

	private string OpenFrame()
	{
		return SentInvokes()
			.Single(raw => raw.Contains($"\"{CapabilityOperations.DeviceProvider.SessionOpen}\"",
				StringComparison.Ordinal));
	}

	private IEnumerable<string> SentInvokes()
	{
		return Link.SentByHost
			.Where(envelope => string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal))
			.Select(envelope => envelope.Payload!.Value.GetRawText());
	}

	private sealed class PaintingDeviceProvider : IPluginIntegration, IDeviceProvider
	{
		private readonly ConcurrentQueue<DeviceKeyImage> _painted = new();

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public IDeviceSession? Session { get; private set; }

		public IReadOnlyList<DeviceKeyImage> Painted => [.. _painted];

		public IReadOnlyList<(int Row, int Column)> FetchOnOpen { get; set; } = [];

		public List<DeviceKeyImage?> Fetched { get; } = [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public async Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			Session = session;
			session.KeyImageChanged += (_, args) => _painted.Enqueue(args.Image);
			foreach (var (row, column) in FetchOnOpen)
			{
				Fetched.Add(await session.GetKeyImageAsync(row, column, cancellationToken));
			}
		}
	}
}
