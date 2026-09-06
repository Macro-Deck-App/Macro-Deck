using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// Icon delivery to a device session. What the icon service derives an ETag from is its own contract
/// (see <c>IconServiceTests</c>); what is asserted here is the part the session owns - which variant it
/// asks for, when it skips the transfer entirely, and what it refuses to deliver.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfaceIconTests
{
	private static readonly byte[] _bytes = [1, 2, 3, 4];

	private DeviceSurfaceFixture _fixture = null!;
	private RecordingIconService _icons = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_icons = new RecordingIconService();
		_fixture = new DeviceSurfaceFixture(_icons);
		_deviceId = await _fixture.OpenDeviceAsync();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task An_icon_is_delivered_with_its_bytes_content_type_and_etag()
	{
		_icons.Set("etag-1", "image/webp", _bytes);

		var icon = await _fixture.Service.GetIconAsync(_deviceId, _icons.IconId.ToString(), size: 72);

		Assert.Multiple(() =>
		{
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon!.Content.ToArray(), Is.EqualTo(_bytes));
			Assert.That(icon.ContentType, Is.EqualTo("image/webp"));
			Assert.That(icon.ETag, Is.EqualTo("etag-1"));
			Assert.That(icon.NotModified, Is.False);
			Assert.That(_icons.Requests.Single(), Is.EqualTo(72));
		});
	}

	[Test]
	public async Task Presenting_the_current_etag_costs_no_bytes_and_keeps_the_same_etag()
	{
		_icons.Set("etag-1", "image/webp", _bytes);

		var icon = await _fixture.Service.GetIconAsync(_deviceId,
			_icons.IconId.ToString(),
			size: 72,
			knownETag: "etag-1");

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.True);
			Assert.That(icon.ETag, Is.EqualTo("etag-1"));
			Assert.That(icon.Content.Length, Is.Zero);
		});
	}

	[Test]
	public async Task A_stale_etag_delivers_the_new_bytes_under_the_new_etag()
	{
		_icons.Set("etag-2", "image/webp", [9, 9, 9]);

		var icon = await _fixture.Service.GetIconAsync(_deviceId,
			_icons.IconId.ToString(),
			size: 72,
			knownETag: "etag-1");

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.False);
			Assert.That(icon.ETag, Is.EqualTo("etag-2"));
			Assert.That(icon.Content.ToArray(), Is.EqualTo(new byte[] { 9, 9, 9 }));
		});
	}

	[Test]
	public async Task Asking_for_no_particular_size_serves_the_largest_rendered_variant_not_the_master()
	{
		_icons.Set("etag-1", "image/webp", _bytes);

		await _fixture.Service.GetIconAsync(_deviceId, _icons.IconId.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(_icons.Requests.Single(),
				Is.EqualTo(512),
				"an unbounded master image would not fit a key, so null must not travel through");
		});
	}

	[Test]
	public void An_icon_over_the_transfer_bound_is_refused_by_a_named_reason()
	{
		_icons.Set("etag-1", "image/webp", new byte[ProtocolLimits.MaxAssetBytes + 1]);

		var exception = Assert.ThrowsAsync<DeviceSessionException>(async ()
			=> await _fixture.Service.GetIconAsync(_deviceId, _icons.IconId.ToString(), size: 512));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ReasonCode, Is.EqualTo(DeviceSessionReasons.IconTooLarge));
			Assert.That(_fixture.Service.IsOpen(_deviceId), Is.True);
		});
	}

	[Test]
	public async Task A_device_with_no_session_and_a_malformed_icon_id_are_both_served_nothing()
	{
		_icons.Set("etag-1", "image/webp", _bytes);

		var noSession = await _fixture.Service.GetIconAsync(Guid.NewGuid(), _icons.IconId.ToString());
		var malformed = await _fixture.Service.GetIconAsync(_deviceId, "not-a-guid");

		Assert.Multiple(() =>
		{
			Assert.That(noSession, Is.Null);
			Assert.That(malformed, Is.Null);
			Assert.That(_icons.Requests, Is.Empty);
		});
	}

	internal sealed class RecordingIconService : IIconService
	{
		private string _eTag = "etag-1";
		private string _contentType = "image/webp";
		private byte[] _content = [];

		public Guid IconId { get; } = Guid.NewGuid();

		public List<int?> Requests { get; } = [];

		public void Set(string eTag, string contentType, byte[] content)
		{
			_eTag = eTag;
			_contentType = contentType;
			_content = content;
		}

		public Task<Result<IconImageResult, IconError>> GetImage(Guid iconId,
			int? size,
			bool acceptWebp,
			bool staticFrame,
			CancellationToken cancellationToken)
		{
			if (iconId != IconId)
			{
				return Task.FromResult(Result.Fail<IconImageResult, IconError>(IconError.NotFound, "no such icon"));
			}

			Requests.Add(size);
			return Task.FromResult(Result.Ok<IconImageResult, IconError>(
				new IconImageResult(new MemoryStream(_content), _eTag, _contentType)));
		}

		public Task<Result<IconEntity, IconError>> Rename(Guid iconId, string name)
			=> throw new NotSupportedException();

		public Task<Result<IconError>> Delete(Guid iconId) => throw new NotSupportedException();

		public Task<Result<int, IconError>> DeleteMany(IReadOnlyList<Guid> iconIds) =>
			throw new NotSupportedException();
	}
}
