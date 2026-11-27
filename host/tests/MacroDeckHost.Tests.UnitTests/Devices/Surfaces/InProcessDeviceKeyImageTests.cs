using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Devices.Surfaces.InProcess;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
public class InProcessDeviceKeyImageTests
{
	private static readonly Guid _deviceId = Guid.Parse("8d0c3c56-1111-4a39-9b3a-9f6d52e5d001");

	private static readonly LayoutKeyImageFormat _png = new() { Encoding = KeyImageEncodings.Png, Rotation = 270 };

	private static readonly long[] _expectedRevisions = [2, 1, 3];

	private KeyImageSurfaces _surfaces = null!;
	private CapturingProvider _provider = null!;
	private InProcessDeviceSurfaceProvider _adapter = null!;

	[SetUp]
	public void SetUp()
	{
		_surfaces = new KeyImageSurfaces();
		_provider = new CapturingProvider();
		_adapter = new InProcessDeviceSurfaceProvider("integration",
			_provider,
			() => _surfaces,
			Serilog.Core.Logger.None);
	}

	[Test]
	public async Task A_session_opened_with_key_images_reports_its_format_and_raises_each_newer_picture_once()
	{
		await _adapter.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), "serial", _png),
			CancellationToken.None);

		await _adapter.PushKeyImagesAsync(_deviceId.ToString(),
			[Image(0, 0, 2), Image(0, 1, 1), Image(0, 0, 2), Image(0, 0, 1)],
			CancellationToken.None);
		await _adapter.PushKeyImagesAsync(_deviceId.ToString(), [Image(0, 0, 3)], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_adapter.SupportsKeyImages, Is.True);
			Assert.That(_provider.Session!.KeyImageFormat, Is.EqualTo(_png));
			Assert.That(_provider.Painted.Select(image => image.Revision), Is.EqualTo(_expectedRevisions));
		});
	}

	[Test]
	public async Task A_cell_is_fetched_from_the_surface_service_only_for_a_session_with_key_images()
	{
		await _adapter.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), "serial", _png),
			CancellationToken.None);
		_surfaces.Next = Image(1, 2, 5);

		var image = await _provider.Session!.GetKeyImageAsync(1, 2);
		await _adapter.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), "serial"),
			CancellationToken.None);
		var withoutKeyImages = await _provider.Session!.GetKeyImageAsync(1, 2);

		Assert.Multiple(() =>
		{
			Assert.That(image, Is.EqualTo(_surfaces.Next));
			Assert.That(withoutKeyImages, Is.Null);
			Assert.That(_provider.Session.KeyImageFormat, Is.Null);
			Assert.That(_surfaces.Requests, Is.EqualTo(new[] { (_deviceId, 1, 2) }));
		});
	}

	[Test]
	public async Task A_pushed_picture_no_newer_than_one_already_fetched_for_its_cell_is_not_raised()
	{
		await _adapter.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), "serial", _png),
			CancellationToken.None);
		_surfaces.Next = Image(1, 2, 5);

		await _provider.Session!.GetKeyImageAsync(1, 2);
		await _adapter.PushKeyImagesAsync(_deviceId.ToString(),
			[Image(1, 2, 4), Image(1, 2, 5)],
			CancellationToken.None);
		await _adapter.PushKeyImagesAsync(_deviceId.ToString(), [Image(1, 2, 6)], CancellationToken.None);

		Assert.That(_provider.Painted.Select(image => image.Revision), Is.EqualTo(new[] { 6L }));
	}

	private static DeviceKeyImage Image(int row, int column, long revision)
	{
		return new DeviceKeyImage
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Png,
			Content = new byte[] { (byte)revision }
		};
	}

	private sealed class CapturingProvider : IDeviceProvider
	{
		public IDeviceSession? Session { get; private set; }

		public List<DeviceKeyImage> Painted { get; } = [];

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
			session.KeyImageChanged += (_, e) => Painted.Add(e.Image);
			return Task.CompletedTask;
		}
	}

	private sealed class KeyImageSurfaces : IDeviceSurfaceService
	{
		public DeviceKeyImage? Next { get; set; }

		public List<(Guid DeviceId, int Row, int Column)> Requests { get; } = [];

		public Task<DeviceKeyImage?> GetKeyImageAsync(
			Guid deviceId,
			int row,
			int column,
			CancellationToken cancellationToken = default)
		{
			Requests.Add((deviceId, row, column));
			return Task.FromResult(Next);
		}

		public bool IsOpen(Guid deviceId)
		{
			return true;
		}

		public Task OpenAsync(
			Guid deviceId,
			string providerId,
			string providerDeviceId,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task CloseAsync(Guid deviceId, string? reason, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<bool> NavigateAsync(
			Guid deviceId,
			DeckNavigationCommand command,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<DeviceInteractionOutcome> SubmitInteractionAsync(
			Guid deviceId,
			DeviceInteraction interaction,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(DeviceInteractionOutcome.Accepted);
		}

		public Task<DeviceIconImage?> GetIconAsync(
			Guid deviceId,
			string iconId,
			int? size = null,
			string? knownETag = null,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<DeviceIconImage?>(null);
		}

		public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
			Guid deviceId,
			string widgetId,
			string? knownETag = null,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<DeviceWidgetIconImage?>(null);
		}

		public Task InvalidateAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task InvalidateAsync(Guid deviceId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task SetPresenceAsync(Guid deviceId, bool online, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}
