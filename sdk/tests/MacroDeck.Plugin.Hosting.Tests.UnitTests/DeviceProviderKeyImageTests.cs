using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class DeviceProviderKeyImageTests
{
	private const string SessionId = "session-1";

	private static readonly byte[] _red = [255, 0, 0, 255];
	private static readonly byte[] _blue = [0, 0, 255, 255];
	private static readonly byte[] _green = [0, 255, 0, 255];

	private static ServiceProvider _services = null!;

	private ScriptedHostInvoker _invoker = null!;
	private PluginHostAssetReceiver _assets = null!;
	private SessionCapturingProvider _provider = null!;
	private DeviceProviderCapabilityHandler _handler = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp()
	{
		_services = new ServiceCollection().BuildServiceProvider();
	}

	[OneTimeTearDown]
	public static void OneTimeTearDown()
	{
		_services.Dispose();
	}

	[SetUp]
	public void SetUp()
	{
		_invoker = new ScriptedHostInvoker();
		_assets = new PluginHostAssetReceiver();
		_provider = new SessionCapturingProvider();
		_handler = new DeviceProviderCapabilityHandler([_provider], TestMetadata.Default, _invoker, _assets);
	}

	[Test]
	public void The_provider_declares_device_provider_version_three()
	{
		var declared = _handler.DeclareCapabilities().Single();

		Assert.Multiple(() =>
		{
			Assert.That(declared.VersionRange.Minimum, Is.EqualTo(1));
			Assert.That(declared.VersionRange.Maximum, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_session_opened_with_key_images_reports_that_format_for_its_life()
	{
		var session = await OpenAsync(new LayoutKeyImageFormatDto
		{
			Encoding = KeyImageEncodings.Jpeg, Rotation = 180, MirrorHorizontal = true
		});

		Assert.That(session.KeyImageFormat,
			Is.EqualTo(new LayoutKeyImageFormat
			{
				Encoding = KeyImageEncodings.Jpeg, Rotation = 180, MirrorHorizontal = true
			}));
	}

	[Test]
	public async Task A_session_the_host_opened_without_key_images_has_no_format_and_asks_the_host_nothing()
	{
		var session = await OpenAsync(null);

		var image = await session.GetKeyImageAsync(0, 0);

		Assert.Multiple(() =>
		{
			Assert.That(session.KeyImageFormat, Is.Null);
			Assert.That(image, Is.Null);
			Assert.That(_invoker.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Each_pushed_cell_is_raised_once_with_its_picture()
	{
		var session = await OpenAsync(Rgba());
		var raised = Record(session);

		var result = await PushAsync(Inline(0, 0, 1, _red), Inline(1, 2, 1, _blue));

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(raised.Select(image => (image.Row, image.Column, image.Revision)),
				Is.EqualTo(new[] { (0, 0, 1L), (1, 2, 1L) }));
			Assert.That(raised.Select(image => image.Content.ToArray()), Is.EqualTo(new[] { _red, _blue }));
			Assert.That(raised.Select(image => image.ContentType), Is.All.EqualTo(KeyImageContentTypes.Rgba));
		});
	}

	[Test]
	public async Task A_picture_whose_revision_is_not_newer_than_the_last_one_for_its_cell_is_dropped()
	{
		var session = await OpenAsync(Rgba());
		var raised = Record(session);

		await PushAsync(Inline(0, 0, 2, _red));
		await PushAsync(Inline(0, 0, 1, _blue), Inline(0, 0, 2, _blue), Inline(0, 1, 1, _blue));
		await PushAsync(Inline(0, 0, 3, _green));

		Assert.That(raised.Select(image => (image.Column, image.Revision, image.Content.ToArray())),
			Is.EqualTo(new[] { (0, 2L, _red), (1, 1L, _blue), (0, 3L, _green) }));
	}

	[Test]
	public async Task A_picture_too_large_to_travel_inline_arrives_over_its_transfer()
	{
		var session = await OpenAsync(Rgba());
		var arrived = new TaskCompletionSource<DeviceKeyImage>(TaskCreationOptions.RunContinuationsAsynchronously);
		session.KeyImageChanged += (_, e) => arrived.TrySetResult(e.Image);
		var large = new byte[200 * 1024];
		large.AsSpan().Fill(7);

		var result = await PushAsync(Transferred(2, 3, 1, "transfer-1"));
		Deliver("transfer-1", large);

		var image = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That((image.Row, image.Column, image.Revision), Is.EqualTo((2, 3, 1L)));
			Assert.That(image.Content.ToArray(), Is.EqualTo(large));
		});
	}

	[Test]
	public async Task Closing_the_session_releases_a_transfer_nobody_will_claim_and_raises_nothing_more()
	{
		var session = await OpenAsync(Rgba());
		var raised = Record(session);
		await PushAsync(Transferred(0, 0, 1, "transfer-1"));
		Begin("transfer-1", _red);

		await Invoke(CapabilityOperations.DeviceProvider.SessionClose,
			new DeviceSessionCloseArguments { SessionId = SessionId });
		var commit = Commit("transfer-1");

		Assert.Multiple(() =>
		{
			Assert.That(commit.Accepted, Is.False);
			Assert.That(raised, Is.Empty);
		});
	}

	[Test]
	public async Task Pictures_for_a_session_this_plugin_no_longer_holds_are_a_no_op()
	{
		var result = await PushAsync(Inline(0, 0, 1, _red));

		Assert.That(result.IsFailure, Is.False);
	}

	[Test]
	public async Task A_host_that_predates_key_images_answers_null_rather_than_throwing()
	{
		var session = await OpenAsync(Rgba());
		_invoker.Failure = new ProtocolError
		{
			Code = ProtocolErrorCodes.CapabilityUnsupported,
			Message = "The 'devices' api has no operation 'key-image'.",
			Retryable = false
		};

		var image = await session.GetKeyImageAsync(0, 0);

		Assert.Multiple(() =>
		{
			Assert.That(image, Is.Null);
			Assert.That(_invoker.Calls.Single(), Is.EqualTo((HostApis.Devices, HostOperations.Devices.KeyImage)));
		});
	}

	[Test]
	public async Task The_current_picture_of_a_cell_is_fetched_from_the_host()
	{
		var session = await OpenAsync(Rgba());
		_invoker.Result = JsonSerializer.SerializeToElement(new DevicesKeyImageResult
			{
				Row = 1,
				Column = 4,
				Revision = 6,
				ContentType = KeyImageContentTypes.Rgba,
				Content = _green
			},
			PluginProtocolJson.Options);

		var image = await session.GetKeyImageAsync(1, 4);

		Assert.Multiple(() =>
		{
			Assert.That((image!.Row, image.Column, image.Revision), Is.EqualTo((1, 4, 6L)));
			Assert.That(image.Content.ToArray(), Is.EqualTo(_green));
			Assert.That(_invoker.LastArguments,
				Is.EqualTo(new DevicesKeyImageArguments { SessionId = SessionId, Row = 1, Column = 4 }));
		});
	}

	[Test]
	public async Task A_pushed_picture_no_newer_than_one_already_fetched_for_its_cell_is_dropped()
	{
		var session = await OpenAsync(Rgba());
		var raised = Record(session);
		_invoker.Result = JsonSerializer.SerializeToElement(new DevicesKeyImageResult
			{
				Row = 0,
				Column = 0,
				Revision = 5,
				ContentType = KeyImageContentTypes.Rgba,
				Content = _green
			},
			PluginProtocolJson.Options);
		var fetched = await session.GetKeyImageAsync(0, 0);

		await PushAsync(Transferred(0, 0, 4, "transfer-late"));
		Deliver("transfer-late", _red);
		await PushAsync(Inline(0, 0, 5, _blue));
		await PushAsync(Inline(0, 0, 6, _blue));

		Assert.Multiple(() =>
		{
			Assert.That(fetched!.Revision, Is.EqualTo(5));
			Assert.That(raised.Select(image => image.Revision), Is.EqualTo(new[] { 6L }));
		});
	}

	[Test]
	public void A_layout_grid_keeps_its_key_image_format_through_the_wire_shape()
	{
		var layout = new LayoutDescriptor("deck",
			"Deck",
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = 3,
						Columns = 5,
						KeySize = new LayoutKeySize(72, 72),
						KeyImages = new LayoutKeyImageFormat
						{
							Encoding = KeyImageEncodings.Bmp, Rotation = 90, MirrorHorizontal = true
						}
					}
				}
			]);

		var wire = LayoutDescriptorMapper.ToDto(layout);
		var json = JsonSerializer.Serialize(wire, PluginProtocolJson.Options);
		var back = LayoutDescriptorMapper.ToSdk(
			JsonSerializer.Deserialize<LayoutDescriptorDto>(json, PluginProtocolJson.Options)!);

		Assert.That(back.Regions.Single().Grid!.KeyImages, Is.EqualTo(layout.Regions.Single().Grid!.KeyImages));
	}

	private static LayoutKeyImageFormatDto Rgba()
	{
		return new LayoutKeyImageFormatDto { Encoding = KeyImageEncodings.Rgba };
	}

	private static DeviceSessionKeyImageDto Inline(int row, int column, long revision, byte[] content)
	{
		return new DeviceSessionKeyImageDto
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Rgba,
			Content = content
		};
	}

	private static DeviceSessionKeyImageDto Transferred(int row, int column, long revision, string transferId)
	{
		return new DeviceSessionKeyImageDto
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Rgba,
			TransferId = transferId
		};
	}

	private static List<DeviceKeyImage> Record(IDeviceSession session)
	{
		var raised = new List<DeviceKeyImage>();
		session.KeyImageChanged += (_, e) => raised.Add(e.Image);
		return raised;
	}

	private async Task<IDeviceSession> OpenAsync(LayoutKeyImageFormatDto? keyImages)
	{
		var result = await Invoke(CapabilityOperations.DeviceProvider.SessionOpen,
			new DeviceSessionOpenArguments
			{
				SessionId = SessionId, DeviceId = "device-1", ProviderDeviceId = "serial-1", KeyImages = keyImages
			});

		var open = result.Data!.Value.Deserialize<DeviceSessionOpenResult>(PluginProtocolJson.Options)!;
		Assert.That(open.Accepted, Is.True);
		return _provider.Session!;
	}

	private Task<CapabilityInvocationResult> PushAsync(params DeviceSessionKeyImageDto[] images)
	{
		return Invoke(CapabilityOperations.DeviceProvider.SessionKeyImages,
			new DeviceSessionKeyImagesArguments { SessionId = SessionId, Images = images });
	}

	private Task<CapabilityInvocationResult> Invoke(string operation, object arguments)
	{
		return _handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.DeviceProvider,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
				CorrelationId = "correlation",
				Services = _services
			},
			CancellationToken.None);
	}

	private void Deliver(string transferId, byte[] content)
	{
		Begin(transferId, content);
		var index = 0;
		foreach (var chunk in content.Chunk(64 * 1024))
		{
			_assets.Handle(Envelope(MessageTypes.HostAssetChunk,
				new HostAssetChunkPayload
				{
					AssetId = transferId, Index = index++, Data = Convert.ToBase64String(chunk)
				}));
		}

		Commit(transferId);
	}

	private void Begin(string transferId, byte[] content)
	{
		_assets.Handle(Envelope(MessageTypes.HostAssetBegin,
			new HostAssetBeginPayload
			{
				AssetId = transferId,
				Kind = AssetKinds.KeyImage,
				MimeType = KeyImageContentTypes.Rgba,
				TotalBytes = content.Length,
				ContentHash = AssetContentHash.Compute(content)
			}));
	}

	private HostAssetAckPayload Commit(string transferId)
	{
		var ack = _assets.Handle(Envelope(MessageTypes.HostAssetCommit,
			new HostAssetCommitPayload { AssetId = transferId }));
		return ack.Payload!.Value.Deserialize<HostAssetAckPayload>(PluginProtocolJson.Options)!;
	}

	private static ProtocolEnvelope Envelope(string type, object payload)
	{
		return new ProtocolEnvelope
		{
			Type = type,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(payload, PluginProtocolJson.Options)
		};
	}

	private sealed class SessionCapturingProvider : IPluginIntegration, IDeviceProvider
	{
		public IDeviceSession? Session { get; private set; }

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

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

		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			Session = session;
			return Task.CompletedTask;
		}
	}

	private sealed class ScriptedHostInvoker : IHostInvoker
	{
		public List<(string Api, string Operation)> Calls { get; } = [];

		public object? LastArguments { get; private set; }

		public JsonElement? Result { get; set; }

		public ProtocolError? Failure { get; set; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Calls.Add((api, operation));
			LastArguments = arguments;
			return Failure is { } failure
				? Task.FromException<JsonElement?>(HostInvocationException.From(failure))
				: Task.FromResult(Result);
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
