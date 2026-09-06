using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// Delivery of a widget's action-icon-provider bytes to a device session - the counterpart to
/// <see cref="DeviceSurfaceIconTests" /> for icon-pack icons. What the icon-provider resolution itself
/// derives an identity from is <c>IWidgetIconService</c>'s own contract; what is asserted here is the
/// part the surface service owns when serving it to a device: the surface-membership guard and the
/// known-version shortcut.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfaceWidgetIconFetchTests
{
	private static readonly byte[] _bytes = [1, 2, 3, 4];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;
	private Guid _widgetId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		_widgetId = Guid.NewGuid();
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button(_widgetId.ToString(), 0, 0));
		_deviceId = await _fixture.OpenDeviceAsync();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private UiResource Register(string mediaType, byte[] content)
		=> _fixture.Resources.Register(new UiResourceRegistration
		{
			OwnerId = "test", Name = _widgetId.ToString("N"), MediaType = mediaType, Content = content
		});

	[Test]
	public async Task An_active_providers_bytes_are_delivered_with_their_content_type_and_version()
	{
		var resource = Register("image/webp", _bytes);
		_fixture.WidgetIcons.Set(_widgetId, WidgetIconResolution.Active(resource));

		var icon = await _fixture.Service.GetWidgetIconAsync(_deviceId, _widgetId.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon!.Content.ToArray(), Is.EqualTo(_bytes));
			Assert.That(icon.ContentType, Is.EqualTo("image/webp"));
			Assert.That(icon.ETag, Is.EqualTo(resource.ContentHash));
			Assert.That(icon.NotModified, Is.False);
		});
	}

	[Test]
	public async Task Presenting_the_current_version_costs_no_bytes_and_keeps_the_same_etag()
	{
		var resource = Register("image/webp", _bytes);
		_fixture.WidgetIcons.Set(_widgetId, WidgetIconResolution.Active(resource));

		var icon = await _fixture.Service.GetWidgetIconAsync(_deviceId, _widgetId.ToString(), resource.ContentHash);

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.True);
			Assert.That(icon.ETag, Is.EqualTo(resource.ContentHash));
			Assert.That(icon.Content.Length, Is.Zero);
		});
	}

	[Test]
	public async Task A_stale_version_delivers_the_new_bytes_under_the_new_identity()
	{
		var resource = Register("image/webp", [9, 9, 9]);
		_fixture.WidgetIcons.Set(_widgetId, WidgetIconResolution.Active(resource));

		var icon = await _fixture.Service.GetWidgetIconAsync(_deviceId, _widgetId.ToString(), "stale-etag");

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.False);
			Assert.That(icon.ETag, Is.EqualTo(resource.ContentHash));
			Assert.That(icon.Content.ToArray(), Is.EqualTo(new byte[] { 9, 9, 9 }));
		});
	}

	[Test]
	public async Task No_active_provider_serves_nothing()
	{
		var icon = await _fixture.Service.GetWidgetIconAsync(_deviceId, _widgetId.ToString());

		Assert.That(icon, Is.Null);
	}

	[Test]
	public async Task A_widget_id_not_on_the_devices_current_surface_serves_nothing()
	{
		var resource = Register("image/webp", _bytes);
		var elsewhere = Guid.NewGuid();
		_fixture.WidgetIcons.Set(elsewhere, WidgetIconResolution.Active(resource));

		var icon = await _fixture.Service.GetWidgetIconAsync(_deviceId, elsewhere.ToString());

		Assert.That(icon, Is.Null);
	}

	[Test]
	public async Task A_device_with_no_open_session_serves_nothing()
	{
		var resource = Register("image/webp", _bytes);
		_fixture.WidgetIcons.Set(_widgetId, WidgetIconResolution.Active(resource));

		var icon = await _fixture.Service.GetWidgetIconAsync(Guid.NewGuid(), _widgetId.ToString());

		Assert.That(icon, Is.Null);
	}
}
