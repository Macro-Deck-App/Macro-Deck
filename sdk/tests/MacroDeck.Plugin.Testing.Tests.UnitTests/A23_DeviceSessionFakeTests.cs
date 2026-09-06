using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A23 - a device provider can be driven through a whole session with no host and no transport: the
/// test pushes the surfaces the host would push and reads back what the provider reported.
/// </summary>
[TestFixture]
public class A23_DeviceSessionFakeTests
{
	private const string DeviceId = "SERIAL-1";

	private static readonly (long, string)[] _expectedRendered = [(1L, "home"), (2L, "lights")];

	private static readonly DeviceInteractionKind[] _expectedKinds =
		[DeviceInteractionKind.Press, DeviceInteractionKind.Release];

	private static readonly string[] _expectedDeviceIds = [DeviceId, DeviceId];

	private static readonly string[] _expectedWidgetIds = ["w1", "w1"];

	private static readonly string?[] _expectedClosures = ["device unplugged"];

	private static readonly byte[] _iconBytes = [1, 2, 3];

	private static readonly int?[] _expectedIconSizes = [72, 72, null];

	private static readonly string[] _expectedWidgetIconRequestIds = ["w1", "w1", "w2"];

	private static DeviceSurface Surface(long revision, string folderId, string label)
		=> new()
		{
			Revision = revision,
			Profile = new DeviceSurfaceProfile("studio", "Studio"),
			Folder = new DeviceSurfaceFolder(folderId, folderId, null, true),
			Layout = new DeviceSurfaceLayout { Rows = 2, Columns = 3, LayoutReference = "com.example::3x2" },
			Widgets =
			[
				new DeviceSurfaceWidget
				{
					Id = "w1",
					Type = "ActionButton",
					PositionX = 0,
					PositionY = 0,
					Width = 1,
					Height = 1,
					Appearance = new DeviceSurfaceAppearance { Label = label },
					SupportedInteractions = [DeviceInteractionKind.Press, DeviceInteractionKind.Release]
				}
			]
		};

	private static async Task<(FakeDeviceProviderContext Context, RenderingProvider Provider, FakeDeviceSession Session
			)>
		OpenAsync()
	{
		var context = new FakeDeviceProviderContext();
		var provider = new RenderingProvider();
		await context.RegisterDeviceAsync(new DeviceDescriptor(DeviceId, "Deck"));
		var session = await context.OpenSession(provider, DeviceId);
		return (context, provider, session);
	}

	[Test]
	public async Task A_provider_renders_every_surface_it_is_pushed_and_its_presses_come_back_in_order()
	{
		var (context, provider, session) = await OpenAsync();

		session.PushSurface(Surface(1, "home", "Idle"));
		session.PushSurface(Surface(2, "lights", "Recording"));

		await provider.PressAsync("w1");
		await provider.ReleaseAsync("w1");

		Assert.Multiple(() =>
		{
			Assert.That(provider.Rendered.Select(surface => (surface.Revision, surface.Folder!.Id)),
				Is.EqualTo(_expectedRendered));
			Assert.That(session.Interactions.Select(interaction => interaction.Kind),
				Is.EqualTo(_expectedKinds));
			Assert.That(context.Interactions.Select(entry => entry.DeviceId),
				Is.EqualTo(_expectedDeviceIds));
			Assert.That(context.Calls.Where(call => call.Kind == DeviceProviderCallKind.Interaction)
					.Select(call => call.Interaction!.Target.WidgetId),
				Is.EqualTo(_expectedWidgetIds));
		});
	}

	[Test]
	public async Task A_stale_widget_id_comes_back_as_a_verdict_the_provider_can_act_on()
	{
		var (_, provider, session) = await OpenAsync();
		session.PushSurface(Surface(1, "home", "Idle"));
		session.NextResult = DeviceInteractionResult.Rejected(DeviceSessionReasons.WidgetNotOnSurface);

		var result = await provider.PressAsync("gone");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(DeviceInteractionStatus.Rejected));
			Assert.That(result.ReasonCode, Is.EqualTo(DeviceSessionReasons.WidgetNotOnSurface));
			Assert.That(result.IsAccepted, Is.False);
		});
	}

	[Test]
	public async Task Closing_a_session_reaches_the_provider_exactly_once()
	{
		var (_, provider, session) = await OpenAsync();

		session.Close("device unplugged");
		session.Close("again");
		await session.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(provider.Closures, Is.EqualTo(_expectedClosures));
			Assert.That(session.IsDisposed, Is.True);
		});
	}

	[Test]
	public async Task A_cached_icon_is_served_without_its_bytes()
	{
		var (_, _, session) = await OpenAsync();
		session.Icons["icon-1"] = new DeviceIconImage
		{
			IconId = "icon-1",
			ContentType = "image/webp",
			ETag = "etag-1",
			Content = _iconBytes,
			NotModified = false
		};

		var fresh = await session.GetIconAsync("icon-1", size: 72);
		var cached = await session.GetIconAsync("icon-1", size: 72, knownETag: "etag-1");
		var missing = await session.GetIconAsync("icon-2");

		Assert.Multiple(() =>
		{
			Assert.That(fresh!.Content.ToArray(), Is.EqualTo(_iconBytes));
			Assert.That(cached!.NotModified, Is.True);
			Assert.That(cached.Content.Length, Is.Zero);
			Assert.That(missing, Is.Null);
			Assert.That(session.IconRequests.Select(request => request.Size), Is.EqualTo(_expectedIconSizes));
		});
	}

	[Test]
	public async Task A_cached_widget_icon_is_served_without_its_bytes()
	{
		var (_, _, session) = await OpenAsync();
		session.WidgetIcons["w1"] = new DeviceWidgetIconImage
		{
			WidgetId = "w1",
			ContentType = "image/png",
			ETag = "sha256:v1",
			Content = _iconBytes,
			NotModified = false
		};

		var fresh = await session.GetWidgetIconAsync("w1");
		var cached = await session.GetWidgetIconAsync("w1", knownETag: "sha256:v1");
		var missing = await session.GetWidgetIconAsync("w2");

		Assert.Multiple(() =>
		{
			Assert.That(fresh!.Content.ToArray(), Is.EqualTo(_iconBytes));
			Assert.That(cached!.NotModified, Is.True);
			Assert.That(cached.Content.Length, Is.Zero);
			Assert.That(missing, Is.Null);
			Assert.That(session.WidgetIconRequests.Select(request => request.WidgetId),
				Is.EqualTo(_expectedWidgetIconRequestIds));
		});
	}

	[Test]
	public void Opening_a_session_for_an_unregistered_device_is_refused()
	{
		var context = new FakeDeviceProviderContext();

		Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await context.OpenSession(new RenderingProvider(), "never-registered"));
	}

	/// <summary>The shape a hardware plugin actually has: keep the session, render every surface, report
	/// what the user did.</summary>
	private sealed class RenderingProvider : IDeviceProvider
	{
		private IDeviceSession? _session;

		public List<DeviceSurface> Rendered { get; } = [];

		public List<string?> Closures { get; } = [];

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			_session = session;
			session.SurfaceChanged += (_, args) => Rendered.Add(args.Surface);
			session.Closed += (_, args) => Closures.Add(args.Reason);
			return Task.CompletedTask;
		}

		public Task<DeviceInteractionResult> PressAsync(string widgetId)
			=> Send(DeviceInteractionKind.Press, widgetId);

		public Task<DeviceInteractionResult> ReleaseAsync(string widgetId)
			=> Send(DeviceInteractionKind.Release, widgetId);

		private Task<DeviceInteractionResult> Send(DeviceInteractionKind kind, string widgetId)
			=> _session!.SendInteractionAsync(new DeviceInteraction
			{
				Kind = kind,
				Target = new DeviceInteractionTarget { WidgetId = widgetId },
				SurfaceRevision = _session.CurrentSurface.Revision
			});
	}
}
