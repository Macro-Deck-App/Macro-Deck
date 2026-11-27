using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A43DeviceKeyImageTests
{
	private const string DeviceId = "SERIAL-1";

	private static readonly byte[] _red = [255, 0, 0, 255];
	private static readonly byte[] _blue = [0, 0, 255, 255];

	private static readonly long[] _expectedRevisions = [1, 2];

	private static readonly LayoutKeyImageFormat _jpeg = new()
	{
		Encoding = KeyImageEncodings.Jpeg, Rotation = 90, CornerRadius = 6
	};

	[Test]
	public async Task A_fake_session_opened_with_a_format_raises_pushed_pictures_and_answers_with_the_latest()
	{
		var context = new FakeDeviceProviderContext { KeyImageFormat = _jpeg };
		var provider = new PaintingProvider();
		await context.RegisterDeviceAsync(new DeviceDescriptor(DeviceId, "Deck"));
		var session = await context.OpenSession(provider, DeviceId);

		session.PushKeyImage(Image(0, 1, 1, _red));
		session.PushKeyImage(Image(0, 1, 2, _blue));
		var current = await session.GetKeyImageAsync(0, 1);
		var missing = await session.GetKeyImageAsync(2, 2);

		Assert.Multiple(() =>
		{
			Assert.That(provider.FormatOnOpen, Is.EqualTo(_jpeg));
			Assert.That(provider.Painted.Select(image => image.Revision), Is.EqualTo(_expectedRevisions));
			Assert.That(current!.Content.ToArray(), Is.EqualTo(_blue));
			Assert.That(missing, Is.Null);
			Assert.That(session.KeyImageRequests, Is.EqualTo(new[] { (0, 1), (2, 2) }));
		});
	}

	[Test]
	public async Task A_fake_session_without_a_format_answers_no_picture_even_when_seeded()
	{
		var context = new FakeDeviceProviderContext();
		var provider = new PaintingProvider();
		await context.RegisterDeviceAsync(new DeviceDescriptor(DeviceId, "Deck"));
		var session = await context.OpenSession(provider, DeviceId);
		session.KeyImages[(0, 0)] = Image(0, 0, 1, _red);

		var image = await session.GetKeyImageAsync(0, 0);

		Assert.Multiple(() =>
		{
			Assert.That(provider.FormatOnOpen, Is.Null);
			Assert.That(image, Is.Null);
		});
	}

	[Test]
	public async Task Over_the_wire_a_provider_gets_the_format_and_one_event_per_cell_without_stale_ones()
	{
		var provider = new PaintingProvider();
		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.RegisterIntegration(_ => provider);
		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		var opened = await session.DeviceProvider.OpenSessionAsync(new DeviceSessionOpenArguments
		{
			SessionId = "session-1",
			DeviceId = "device-1",
			ProviderDeviceId = DeviceId,
			KeyImages = new LayoutKeyImageFormatDto
			{
				Encoding = KeyImageEncodings.Jpeg, Rotation = 90, CornerRadius = 6
			}
		});
		var pushed = await session.DeviceProvider.PushKeyImagesAsync("session-1",
			[Wire(0, 0, 2, _blue), Wire(0, 0, 1, _red), Wire(0, 1, 1, _red)]);
		var unknown = await session.DeviceProvider.PushKeyImagesAsync("no-such-session", [Wire(0, 0, 3, _red)]);

		Assert.Multiple(() =>
		{
			Assert.That(opened.DataAs<DeviceSessionOpenResult>()!.Accepted, Is.True);
			Assert.That(pushed.Succeeded, Is.True, pushed.Error?.Message);
			Assert.That(unknown.Succeeded, Is.True, unknown.Error?.Message);
			Assert.That(provider.FormatOnOpen, Is.EqualTo(_jpeg));
			Assert.That(provider.Painted.Select(image => (image.Row, image.Column, image.Revision)),
				Is.EqualTo(new[] { (0, 0, 2L), (0, 1, 1L) }));
			Assert.That(provider.Painted[0].Content.ToArray(), Is.EqualTo(_blue));
		});
	}

	[Test]
	public async Task A_session_opened_the_old_way_has_no_key_images()
	{
		var provider = new PaintingProvider();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => provider));

		await harness.DeviceProvider.OpenSessionAsync("session-1", "device-1", DeviceId);

		Assert.That(provider.FormatOnOpen, Is.Null);
	}

	private static DeviceKeyImage Image(int row, int column, long revision, byte[] content)
	{
		return new DeviceKeyImage
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Jpeg,
			Content = content
		};
	}

	private static DeviceSessionKeyImageDto Wire(int row, int column, long revision, byte[] content)
	{
		return new DeviceSessionKeyImageDto
		{
			Row = row,
			Column = column,
			Revision = revision,
			ContentType = KeyImageContentTypes.Jpeg,
			Content = content
		};
	}

	private sealed class PaintingProvider : IPluginIntegration, IDeviceProvider
	{
		public IDeviceSession? Session { get; private set; }

		public LayoutKeyImageFormat? FormatOnOpen { get; private set; }

		public List<DeviceKeyImage> Painted { get; } = [];

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
			FormatOnOpen = session.KeyImageFormat;
			session.KeyImageChanged += (_, e) => Painted.Add(e.Image);
			return Task.CompletedTask;
		}
	}
}
