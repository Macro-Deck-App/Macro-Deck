using System.Text;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceKeyImageResourceHoldTests
{
	private const string SlowPrefix = "slow.";

	private static readonly TimeSpan _poll = TimeSpan.FromSeconds(5);

	private SlowPluginIcons _pluginIcons = null!;
	private DeviceSurfaceFixture _fixture = null!;
	private string _speaking = null!;
	private string _silent = null!;

	[SetUp]
	public void SetUp()
	{
		_pluginIcons = new SlowPluginIcons();
		_fixture = new DeviceSurfaceFixture(pluginIcons: _pluginIcons);
		_speaking = Register("speaking", [1]);
		_silent = Register("silent", [2]);
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
	}

	[TearDown]
	public void TearDown()
	{
		_fixture.Dispose();
	}

	private string Register(string name, byte[] content)
	{
		return _fixture.Resources.Register(new UiResourceRegistration
			{
				OwnerId = "test", Name = name, MediaType = "image/png", Content = content
			})
			.ResourceId;
	}

	private static UiRawJson Tree(int revision, string text, string resourceId)
	{
		var root = $$$$"""
					   {"id":"root","type":"ui.stack","children":[
					   	{"id":"i","type":"ui.image","properties":{"source":{"resourceId":"{{{{resourceId}}}}"}}},
					   	{"id":"t","type":"ui.text","properties":{"text":"{{{{text}}}}"}}]}
					   """;
		return new UiRawJson(UiPayloads.TreeWithRoot(revision, root));
	}

	private static string Snapshot()
	{
		return LayoutSnapshotSerializer.Serialize(new LayoutDescriptor("com.example.deck::3x5",
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
						KeyImages = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Jpeg }
					}
				}
			],
			new LayoutCapabilities { Visuals = LayoutVisualCapabilities.Full }));
	}

	private async Task<Guid> OpenAsync(string resourceId)
	{
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "First", resourceId);
		var deviceId = _fixture.AddDevice("DeckA", layoutSnapshot: Snapshot());
		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckA");
		return deviceId;
	}

	private void Publish(int revision, string text, string resourceId)
	{
		_fixture.UiBroker.PublishHostSnapshot("session-1", Tree(revision, text, resourceId), revision);
	}

	private IEnumerable<string> Pushed(Guid deviceId)
	{
		return _fixture.Provider.KeyImageBatchesFor(deviceId)
			.SelectMany(batch => batch)
			.Where(image => image is { Row: 0, Column: 0 })
			.Select(Text);
	}

	private int WidgetRenders()
	{
		return _fixture.KeyImageRenderer.Requests.Count(request => request.Kind == DeviceKeyCellKind.Widget);
	}

	private static string Text(DeviceKeyImage image)
	{
		return Encoding.UTF8.GetString(image.Content.Span);
	}

	private static string Shown(string resourceId)
	{
		return $"res:{resourceId}=shown";
	}

	private static string Missing(string resourceId)
	{
		return $"res:{resourceId}=missing";
	}

	private async Task SettleWithinAsync(Func<bool> condition, TimeSpan budget, string message)
	{
		var deadline = DateTime.UtcNow + _poll;
		var advanced = TimeSpan.Zero;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(message);
			}

			if (advanced < budget)
			{
				var step = TimeSpan.FromMilliseconds(50);
				_fixture.Time.Advance(step);
				advanced += step;
			}

			await Task.Delay(5);
		}
	}

	private Task KeyShowsAsync(Guid deviceId, string text, string resourceState)
	{
		return SettleWithinAsync(() => Pushed(deviceId).LastOrDefault() is { } last
				&& last.Contains(text, StringComparison.Ordinal)
				&& last.Contains(resourceState, StringComparison.Ordinal),
			TimeSpan.FromMilliseconds(500),
			$"The key never showed '{text}' with '{resourceState}'.");
	}

	[Test]
	public async Task A_tile_that_swaps_to_an_image_not_loaded_yet_keeps_its_picture_until_the_image_is_drawn()
	{
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));

		Publish(2, "Second", _speaking);
		await KeyShowsAsync(deviceId, "Second", Shown(_speaking));

		Assert.That(Pushed(deviceId).Where(text => text.Contains(Missing(_speaking), StringComparison.Ordinal)),
			Is.Empty,
			"no picture without the image was pushed while a previous picture existed");
	}

	[Test]
	public async Task An_indicator_that_alternates_between_two_images_draws_each_from_the_cache_after_the_first_time()
	{
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));
		Publish(2, "Second", _speaking);
		await KeyShowsAsync(deviceId, "Second", Shown(_speaking));
		var lookups = _fixture.KeyImageRenderer.Lookups.Count;

		Publish(3, "Third", _silent);
		await KeyShowsAsync(deviceId, "Third", Shown(_silent));
		Publish(4, "Fourth", _speaking);
		await KeyShowsAsync(deviceId, "Fourth", Shown(_speaking));

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.KeyImageRenderer.Lookups.Skip(lookups).Where(lookup => !lookup.Found),
				Is.Empty,
				"an image shown moments ago is still cached");
			Assert.That(Pushed(deviceId).Where(text => text.Contains("=missing", StringComparison.Ordinal)),
				Is.Empty);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_image_that_never_arrives_holds_the_key_no_longer_than_one_second()
	{
		var slow = SlowPrefix + "never";
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));
		var before = Pushed(deviceId).Count();

		Publish(2, "Second", slow);
		await SettleWithinAsync(() => _pluginIcons.Asked.Contains(slow),
			TimeSpan.FromMilliseconds(100),
			"The image was never fetched.");
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(500));
		await Task.Delay(150);
		var held = Pushed(deviceId).Count();

		await SettleWithinAsync(() => Pushed(deviceId).LastOrDefault()?.Contains("Second", StringComparison.Ordinal)
				== true,
			TimeSpan.FromSeconds(1),
			"The key stayed on its old picture past the deadline.");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(held, Is.EqualTo(before), "the key is held while the image may still arrive");
			Assert.That(Pushed(deviceId).Last(), Does.Contain(Missing(slow)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_image_that_never_arrives_is_not_waited_for_again_once_the_key_showed_it_missing()
	{
		var slow = SlowPrefix + "never";
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));
		Publish(2, "Second", slow);
		await SettleWithinAsync(() => Pushed(deviceId).LastOrDefault()?.Contains("Second", StringComparison.Ordinal)
				== true,
			TimeSpan.FromSeconds(2),
			"The key stayed on its old picture past the deadline.");

		Publish(3, "Third", slow);

		await KeyShowsAsync(deviceId, "Third", Missing(slow));
	}

	[Test]
	public async Task An_image_that_does_not_exist_does_not_hold_the_key()
	{
		var deviceId = await OpenAsync("test.absent");
		await KeyShowsAsync(deviceId, "First", Missing("test.absent"));
		await Task.Delay(150);
		var renders = WidgetRenders();

		Publish(2, "Second", "test.absent");
		await KeyShowsAsync(deviceId, "Second", Missing("test.absent"));
		await Task.Delay(150);

		Assert.That(WidgetRenders() - renders, Is.EqualTo(1), "drawn in the pass that got the tree");
	}

	[Test]
	public async Task A_tile_idle_for_longer_than_the_cache_window_still_draws_its_image_from_the_cache()
	{
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));
		_fixture.Time.Advance(DeviceKeyImageResources.RetainFor + TimeSpan.FromSeconds(5));
		await Task.Delay(50);
		var lookups = _fixture.KeyImageRenderer.Lookups.Count;

		Publish(2, "Second", _silent);
		await KeyShowsAsync(deviceId, "Second", Shown(_silent));

		Assert.That(_fixture.KeyImageRenderer.Lookups.Skip(lookups).Where(lookup => !lookup.Found), Is.Empty);
	}

	[Test]
	public async Task An_image_registered_again_with_new_bytes_is_drawn_with_them_when_the_tile_swaps_back()
	{
		var deviceId = await OpenAsync(_silent);
		await KeyShowsAsync(deviceId, "First", Shown(_silent));
		Publish(2, "Second", _speaking);
		await KeyShowsAsync(deviceId, "Second", Shown(_speaking));
		var replaced = _fixture.Resources.Register(new UiResourceRegistration
		{
			OwnerId = "test", Name = "silent", MediaType = "image/png", Content = new byte[] { 7, 7 }
		});

		Publish(3, "Third", _silent);

		await KeyShowsAsync(deviceId, "Third", $"{Shown(_silent)}@{replaced.ContentHash}");
	}

	[Test]
	public async Task The_first_picture_is_not_held_for_an_image_that_is_still_loading()
	{
		var slow = SlowPrefix + "first";

		var deviceId = await OpenAsync(slow);

		await KeyShowsAsync(deviceId, "First", Missing(slow));
	}

	private sealed class SlowPluginIcons : IPluginIconUiResources
	{
		private readonly Lock _gate = new();
		private readonly List<string> _asked = [];

		public IReadOnlyList<string> Asked
		{
			get
			{
				lock (_gate)
				{
					return [.. _asked];
				}
			}
		}

		public Task<PluginIconResourceResult> GetHandleAsync(
			string pluginId,
			string key,
			string name,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new PluginIconResourceResult(PluginIconResourceStatus.NotFound));
		}

		public Task<PluginIconResourceResult> GetHandleAsync(Guid iconId, CancellationToken cancellationToken)
		{
			return Task.FromResult(new PluginIconResourceResult(PluginIconResourceStatus.NotFound));
		}

		public async Task<UiResourceContent?> TryGetAsync(string resourceId, CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				_asked.Add(resourceId);
			}

			if (!resourceId.StartsWith(SlowPrefix, StringComparison.Ordinal))
			{
				return null;
			}

			await Task.Delay(Timeout.Infinite, cancellationToken);
			return null;
		}
	}
}
