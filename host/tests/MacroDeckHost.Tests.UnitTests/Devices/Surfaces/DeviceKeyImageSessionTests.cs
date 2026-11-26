using System.Text;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces.InProcess;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceKeyImageSessionTests
{
	private const int HardwareRows = 3;

	private const int HardwareColumns = 5;

	private static readonly TimeSpan _poll = TimeSpan.FromSeconds(5);

	private DeviceSurfaceFixture _fixture = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
	}

	[TearDown]
	public void TearDown()
	{
		_fixture.Dispose();
	}

	private static string Snapshot(
		LayoutKeyImageFormat? format = null,
		LayoutVisualCapabilities? visuals = null,
		int grids = 1,
		bool keySize = true)
	{
		var regions = Enumerable.Range(0, grids)
			.Select(index => new LayoutRegion
			{
				Id = $"keys-{index}",
				Kind = LayoutRegionKinds.Grid,
				Grid = new LayoutGrid
				{
					Rows = HardwareRows,
					Columns = HardwareColumns,
					KeySize = keySize ? new LayoutKeySize(72, 72) : null,
					KeyImages = format ?? new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Jpeg }
				}
			})
			.ToList();
		return LayoutSnapshotSerializer.Serialize(new LayoutDescriptor("com.example.deck::3x5",
			"Deck",
			regions,
			new LayoutCapabilities { Visuals = visuals ?? LayoutVisualCapabilities.Full }));
	}

	private async Task<Guid> OpenAsync(string? snapshot = null)
	{
		var deviceId = _fixture.AddDevice("DeckA", layoutSnapshot: snapshot ?? Snapshot());
		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckA");
		return deviceId;
	}

	private static UiRawJson Tree(int revision, string text)
	{
		var root = $$$"""
					  {"id":"root","type":"ui.stack","children":[
					  	{"id":"t","type":"ui.text","properties":{"text":"{{{text}}}"}}]}
					  """;
		return new UiRawJson(UiPayloads.TreeWithRoot(revision, root));
	}

	private string SessionOf(string widgetId)
	{
		lock (_fixture.UiOpener.OpenedWidgetIds)
		{
			return $"session-{_fixture.UiOpener.OpenedWidgetIds.LastIndexOf(widgetId) + 1}";
		}
	}

	private IReadOnlyList<IReadOnlyList<DeviceKeyImage>> Batches(Guid deviceId)
	{
		return _fixture.Provider.KeyImageBatchesFor(deviceId);
	}

	private static IEnumerable<(int Row, int Column)> Cells()
	{
		return Enumerable.Range(0, HardwareRows)
			.SelectMany(row => Enumerable.Range(0, HardwareColumns).Select(column => (row, column)));
	}

	private static string Text(DeviceKeyImage image)
	{
		return Encoding.UTF8.GetString(image.Content.Span);
	}

	private static DeviceKeyImage Cell(IReadOnlyList<DeviceKeyImage> batch, int row, int column)
	{
		return batch.Single(image => image.Row == row && image.Column == column);
	}

	private async Task SettleUntilAsync(Func<bool> condition, string message)
	{
		var deadline = DateTime.UtcNow + _poll;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(message);
			}

			_fixture.Time.Advance(TimeSpan.FromMilliseconds(250));
			await Task.Delay(5);
		}
	}

	private static async Task EventuallyAsync(Func<bool> condition, string message)
	{
		var deadline = DateTime.UtcNow + _poll;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(message);
			}

			await Task.Delay(5);
		}
	}

	private async Task KeyShowsAsync(Guid deviceId, int row, int column, string text)
	{
		var deadline = DateTime.UtcNow + _poll;
		while (await _fixture.Service.GetKeyImageAsync(deviceId, row, column) is not { } image
			|| !Text(image).Contains(text, StringComparison.Ordinal))
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail($"Key ({row}, {column}) never showed '{text}'.");
			}

			_fixture.Time.Advance(TimeSpan.FromMilliseconds(250));
			await Task.Delay(5);
		}
	}

	private static async Task QuietAsync()
	{
		await Task.Delay(150);
	}

	[Test]
	public async Task Every_hardware_key_gets_a_picture_on_open_the_empty_and_the_unreached_ones_included()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));

		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		var first = Batches(deviceId)[0];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.Provider.Opens[0].KeyImages?.Encoding, Is.EqualTo(KeyImageEncodings.Jpeg));
			Assert.That(first, Has.Count.EqualTo(HardwareRows * HardwareColumns), "one batch with every key");
			Assert.That(first.Select(image => image.Revision), Is.All.EqualTo(1));
			Assert.That(Text(Cell(first, 0, 0)), Does.StartWith("Widget|ActionButton|"));
			Assert.That(Text(Cell(first, 1, 2)), Does.StartWith("Empty|"), "inside the folder's 2 x 3 grid");
			Assert.That(Text(Cell(first, 0, 4)), Does.StartWith("Background|"), "outside the folder's grid");
			Assert.That(Text(Cell(first, 2, 0)), Does.StartWith("Background|"));
			Assert.That(_fixture.UiOpener.Principals, Is.All.EqualTo($"device-render:{deviceId:N}"));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Every_key_can_be_asked_for_right_after_the_session_opens()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "Hello");

		var deviceId = await OpenAsync();
		var cells = Enumerable.Range(0, HardwareRows)
			.SelectMany(row => Enumerable.Range(0, HardwareColumns).Select(column => (row, column)))
			.Select(cell => _fixture.Service.GetKeyImageAsync(deviceId, cell.row, cell.column))
			.ToList();
		var images = await Task.WhenAll(cells).WaitAsync(_poll);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(images, Has.All.Not.Null);
			Assert.That(Text(images[0]!), Does.Contain("Hello"), "drawn from the widget's tree");
			Assert.That(await _fixture.Service.GetKeyImageAsync(deviceId, HardwareRows, 0), Is.Null);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_tree_change_pushes_only_the_key_it_changes()
	{
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("w1", 0, 0),
			DeviceSurfaceFixture.Button("w2", 1, 0)
		]);
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var surfaces = _fixture.Provider.PushCount(deviceId);

		_fixture.UiBroker.PublishHostSnapshot(SessionOf("w2"), Tree(2, "Live"), 2);
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The changed tree was never drawn.");

		var second = Batches(deviceId)[1];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(second.Select(image => (image.Row, image.Column)), Is.EqualTo(new[] { (0, 1) }));
			Assert.That(second[0].Revision, Is.EqualTo(2));
			Assert.That(Text(second[0]), Does.Contain("Live"));
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(surfaces), "the surface did not change");
			await Task.CompletedTask;
		});
	}

	[TestCase("""{"label":"Button","border":{"style":"static","color":"#00ff00"}}""")]
	[TestCase("""{"label":"Button","backgroundColor":"transparent"}""")]
	public async Task An_edit_the_surface_does_not_carry_still_redraws_the_widget_s_key(string data)
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0, withFlows: false));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var surfaces = _fixture.Provider.PushCount(deviceId);

		await _fixture.MutateAsync(() => _fixture.Home.Widgets[0].Data = data);
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The edit was never drawn.");

		var second = Batches(deviceId)[1];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(second.Select(image => (image.Row, image.Column)), Is.EqualTo(new[] { (0, 0) }));
			Assert.That(Text(second[0]), Does.Contain(data));
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(surfaces), "the surface did not change");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_label_edit_reaches_the_device_as_its_surface_first_and_then_its_picture()
	{
		var widgetId = Guid.NewGuid();
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button(widgetId.ToString(), 0, 0));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var before = _fixture.Provider.Sequence.Count;

		_fixture.Labels.Set(widgetId, string.Empty, "Renamed");
		await _fixture.Service.OnWidgetLabelChanged(widgetId, string.Empty);
		_fixture.UiBroker.PublishHostSnapshot(SessionOf(widgetId.ToString()), Tree(2, "Renamed"), 2);
		await QuietAsync();
		var beforeTheRebuild = _fixture.Provider.Sequence.Count;
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The edit was never drawn.");
		await QuietAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(beforeTheRebuild, Is.EqualTo(before), "the picture waits for the surface it belongs to");
			Assert.That(_fixture.Provider.Sequence.Skip(before), Is.EqualTo(new[] { "surface", "keys" }));
			Assert.That(_fixture.Provider.Latest(deviceId).Widgets.Single().Appearance!.Label,
				Is.EqualTo("Renamed"));
			Assert.That(Text(Batches(deviceId)[^1].Single()), Does.Contain("Renamed"));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_change_only_the_picture_shows_is_pushed_at_once_and_alone()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var before = _fixture.Provider.Sequence.Count;

		_fixture.UiBroker.PublishHostSnapshot(SessionOf("w1"), Tree(2, "Live"), 2);
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(40));
		await EventuallyAsync(() => Batches(deviceId).Count > 1, "The changed picture waited for nothing.");

		Assert.That(_fixture.Provider.Sequence.Skip(before), Is.EqualTo(new[] { "keys" }));
	}

	[Test]
	public async Task An_appearance_change_redraws_every_key()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		_fixture.Preferences.Appearance = new AppearanceSettings("light", "#ff0000");
		await new AppearanceChangedKeyImageHandler(_fixture.Service)
			.Handle(new AppearanceChangedNotification("light", "#ff0000", string.Empty), CancellationToken.None);
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The new appearance was never drawn.");

		var second = Batches(deviceId)[1];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(second, Has.Count.EqualTo(HardwareRows * HardwareColumns));
			Assert.That(second.Select(Text), Has.All.Contains("|light|"));
			Assert.That(second.Select(image => image.Revision), Is.All.EqualTo(2));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_offline_device_is_pushed_nothing_and_gets_every_key_again_when_it_is_back()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		await _fixture.Service.SetPresenceAsync(deviceId, false);
		_fixture.UiBroker.PublishHostSnapshot(SessionOf("w1"), Tree(2, "Away"), 2);
		await KeyShowsAsync(deviceId, 0, 0, "Away");
		await QuietAsync();
		var whileOffline = Batches(deviceId).Count;
		var sequence = _fixture.Provider.Sequence.Count;

		await _fixture.Service.SetPresenceAsync(deviceId, true);
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "Nothing was pushed when the device came back.");

		var back = Batches(deviceId)[1];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(whileOffline, Is.EqualTo(1), "nothing pushed while offline");
			Assert.That(_fixture.Provider.Sequence.Skip(sequence).Take(2), Is.EqualTo(new[] { "surface", "keys" }));
			Assert.That(back, Has.Count.EqualTo(HardwareRows * HardwareColumns));
			Assert.That(Text(Cell(back, 0, 0)), Does.Contain("Away"));
			Assert.That(Cell(back, 2, 4).Revision, Is.EqualTo(2), "an unchanged key is pushed again too");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Keys_a_smaller_folder_or_no_profile_frees_are_drawn_as_background()
	{
		var small = new Folder
		{
			Id = "small",
			Name = "Small",
			ProfileId = DeviceSurfaceFixture.StudioProfileId,
			ParentId = DeviceSurfaceFixture.HomeFolderId,
			Rows = 1,
			Columns = 2
		};
		_fixture.SetFolders(DeviceSurfaceFixture.StudioProfileId,
			_fixture.Home,
			_fixture.Lights,
			_fixture.Scenes,
			_fixture.Audio,
			small);
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 2, 1));
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		await _fixture.ChangeToAsync(deviceId, "small");
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The smaller folder was never drawn.");
		var navigated = Batches(deviceId)[1];

		await _fixture.MutateAsync(() => _fixture.Devices.Devices.Single(device => device.Id == deviceId)
			.StartupProfileId = null);
		await SettleUntilAsync(() => Batches(deviceId).Count > 2, "The unassigned device was never drawn.");
		var unassigned = Batches(deviceId)[2];

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(Text(Cell(navigated, 1, 2)), Does.StartWith("Background|"), "the widget's key is freed");
			Assert.That(Text(Cell(navigated, 1, 0)), Does.StartWith("Background|"), "row 1 is outside 1 x 2");
			Assert.That(navigated.Any(image => image is { Row: 0, Column: 0 }), Is.False, "still an empty key");
			Assert.That(unassigned.Select(Text), Has.All.StartWith("Background|"));
			Assert.That(unassigned.Select(image => (image.Row, image.Column)), Does.Contain((0, 0)));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_widget_whose_tree_never_comes_is_drawn_without_it_after_one_second()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var deviceId = await OpenAsync();

		await QuietAsync();
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(900));
		await QuietAsync();
		var early = Batches(deviceId).Count;
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(150));
		await EventuallyAsync(() => Batches(deviceId).Count > 0, "The key was never drawn after the deadline.");

		var first = Batches(deviceId)[0];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(early, Is.Zero, "no key is pushed before every widget had its second");
			Assert.That(Text(Cell(first, 0, 0)), Does.StartWith("Widget|ActionButton||"), "drawn with no tree");
			await Task.CompletedTask;
		});

		_fixture.UiBroker.PublishHostSnapshot(SessionOf("w1"), Tree(1, "Late"), 1);
		await SettleUntilAsync(() => Batches(deviceId).Count > 1, "The late tree was never drawn.");
		Assert.That(Text(Batches(deviceId)[1].Single()), Does.Contain("Late"));
	}

	[Test]
	public async Task Updates_are_capped_at_the_layout_s_rate_and_coalesced()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "One");
		var twice = LayoutVisualCapabilities.Full with { MaxUpdatesPerSecond = 2 };
		var deviceId = await OpenAsync(Snapshot(visuals: twice));
		await EventuallyAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		_fixture.UiBroker.PublishHostSnapshot("session-1", Tree(2, "Two"), 2);
		_fixture.UiBroker.PublishHostSnapshot("session-1", Tree(3, "Three"), 3);
		await QuietAsync();
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(400));
		await QuietAsync();
		var withinTheCap = Batches(deviceId).Count;
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(100));
		await EventuallyAsync(() => Batches(deviceId).Count > 1, "Nothing was pushed once the cap allowed it.");
		await QuietAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(withinTheCap, Is.EqualTo(1), "half a second has not passed since the last push");
			Assert.That(Batches(deviceId), Has.Count.EqualTo(2), "the two changes are one push");
			Assert.That(Text(Batches(deviceId)[1].Single()), Does.Contain("Three"));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_ticking_widget_is_redrawn_when_its_picture_changes_and_alone()
	{
		_fixture.KeyImageRenderer.TickingTypes.Add("Clock");
		var clock = DeviceSurfaceFixture.Button("clock", 1, 0, withFlows: false);
		clock.Type = "Clock";
		_fixture.Home.Widgets.AddRange([DeviceSurfaceFixture.Button("w1", 0, 0), clock]);
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var pushed = Batches(deviceId).Count;

		await SettleUntilAsync(() => Batches(deviceId).Count > pushed, "The clock never ticked.");

		Assert.That(Batches(deviceId)[pushed].Select(image => (image.Row, image.Column)),
			Is.EqualTo(new[] { (0, 1) }));
	}

	[Test]
	public async Task A_patch_the_host_cannot_apply_asks_for_a_fresh_tree()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "One");
		var deviceId = await OpenAsync();
		await EventuallyAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		var patch = """{"fromRevision":1,"toRevision":2,"operations":[{"op":"remove-node","nodeId":"missing"}]}""";
		_fixture.UiBroker.PublishHostPatch("session-1", new UiRawJson(Encoding.UTF8.GetBytes(patch)), 1, 2);

		Assert.That(_fixture.UiBroker.HostResyncs, Is.EqualTo(new[] { "session-1" }));
	}

	[Test]
	public async Task A_widget_that_leaves_the_keys_has_its_tree_session_closed_and_a_reconfigured_one_reopens()
	{
		_fixture.Home.Widgets.AddRange([
			DeviceSurfaceFixture.Button("w1", 0, 0),
			DeviceSurfaceFixture.Button("w2", 1, 0)
		]);
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "One");
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		_fixture.UiBroker.EndHostSession("session-1", true);
		await EventuallyAsync(() => _fixture.UiOpener.OpenedWidgetIds.Count == 3, "The widget was never reopened.");
		await _fixture.MutateAsync(() => _fixture.Home.Widgets.RemoveAt(1));
		await _fixture.Service.CloseAsync(deviceId, null);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.UiOpener.OpenedWidgetIds, Is.EqualTo(new[] { "w1", "w2", "w1" }));
			Assert.That(_fixture.UiBroker.ClosedSessions, Is.EquivalentTo(new[] { "session-2", "session-3" }));
			Assert.That(_fixture.UiBroker.ObservedSessions, Is.Empty);
			await Task.CompletedTask;
		});
	}

	[TestCase(45, KeyImageEncodings.Jpeg, true, 1)]
	[TestCase(0, "webp", true, 1)]
	[TestCase(0, KeyImageEncodings.Jpeg, false, 1)]
	[TestCase(0, KeyImageEncodings.Jpeg, true, 2)]
	public async Task A_format_the_host_cannot_honour_or_no_single_grid_means_surfaces_only(
		int rotation,
		string encoding,
		bool keySize,
		int grids)
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		var snapshot = Snapshot(new LayoutKeyImageFormat { Encoding = encoding, Rotation = rotation },
			grids: grids,
			keySize: keySize);

		var deviceId = await OpenAsync(snapshot);
		await _fixture.SettleAsync();
		await QuietAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.Provider.Opens.Single().KeyImages, Is.Null);
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1), "the surface still arrives");
			Assert.That(Batches(deviceId), Is.Empty);
			Assert.That(await _fixture.Service.GetKeyImageAsync(deviceId, 0, 0), Is.Null);
			Assert.That(_fixture.UiOpener.OpenedWidgetIds, Is.Empty);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_provider_without_key_images_gets_surfaces_only()
	{
		_fixture.Provider.SupportsKeyImages = false;
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));

		var deviceId = await OpenAsync();
		await _fixture.SettleAsync();
		await QuietAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.Provider.Opens.Single().KeyImages, Is.Null);
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.EqualTo(1));
			Assert.That(Batches(deviceId), Is.Empty);
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_layout_whose_visuals_turn_them_off_draws_square_unspaced_keys_without_rings()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0, 2));
		var deviceId = await OpenAsync(Snapshot(visuals: new LayoutVisualCapabilities()));
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		var widget = _fixture.KeyImageRenderer.Requests.Last(request => request.Kind == DeviceKeyCellKind.Widget);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That((widget.Columns, widget.Rows), Is.EqualTo((2, 1)));
			Assert.That((widget.CornerRadius, widget.Spacing), Is.EqualTo((0d, 0d)));
			Assert.That((widget.Borders, widget.AnimatedIcons), Is.EqualTo((false, false)));
			Assert.That(Text(Cell(Batches(deviceId)[0], 0, 1)), Does.EndWith("|0,1"), "the tile's second key");
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task Changed_fonts_redraw_every_key()
	{
		var deviceId = await OpenAsync();
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");
		var requests = _fixture.KeyImageRenderer.Requests.Count;

		_fixture.KeyImageRenderer.RaiseFontsChanged();
		await SettleUntilAsync(() => _fixture.KeyImageRenderer.Requests.Count >= requests * 2,
			"The keys were not drawn again.");

		Assert.That(Batches(deviceId), Has.Count.EqualTo(1), "an unchanged picture is never pushed again");
	}

	[Test]
	public async Task A_provider_that_asks_for_every_key_while_it_opens_the_session_gets_every_picture()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "Hello");
		var fetched = new List<DeviceKeyImage?>();
		var pushedWhileOpening = -1;
		_fixture.Provider.OnOpen = async descriptor =>
		{
			var opening = Guid.Parse(descriptor.DeviceId);
			foreach (var (row, column) in Cells())
			{
				fetched.Add(await _fixture.Service.GetKeyImageAsync(opening, row, column).WaitAsync(_poll));
			}

			pushedWhileOpening = _fixture.Provider.Sequence.Count;
		};

		var deviceId = await OpenAsync().WaitAsync(_poll);
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "No key image was ever pushed.");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(fetched, Has.Count.EqualTo(HardwareRows * HardwareColumns).And.All.Not.Null);
			Assert.That(Text(fetched[0]!), Does.Contain("Hello"));
			Assert.That(fetched.Select(image => image!.Revision), Is.All.EqualTo(1));
			Assert.That(pushedWhileOpening, Is.Zero, "nothing reaches the provider before it opened the session");
			Assert.That(_fixture.Provider.Sequence.Take(2), Is.EqualTo(new[] { "surface", "keys" }));
			Assert.That(_fixture.Provider.SurfacesFor(deviceId)[0].Revision, Is.EqualTo(1));
			Assert.That(Batches(deviceId)[0].Select(image => image.Revision), Is.All.EqualTo(1));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_in_process_provider_that_asks_for_every_key_while_its_session_opens_gets_every_picture()
	{
		_fixture.Dispose();
		var provider = new FetchingDeviceProvider();
		_fixture = new DeviceSurfaceFixture(provider: fixture => new InProcessDeviceSurfaceProvider("test-provider",
			provider,
			() => fixture.Service,
			Serilog.Core.Logger.None));
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "Hello");

		var deviceId = await OpenAsync().WaitAsync(_poll);
		await QuietAsync();
		var paintedAfterOpen = provider.Painted.Count;
		_fixture.UiBroker.PublishHostSnapshot("session-1", Tree(2, "Live"), 2);
		await SettleUntilAsync(() => provider.Painted.Count > paintedAfterOpen, "The changed key never arrived.");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(_fixture.Service.IsOpen(deviceId), Is.True);
			Assert.That(provider.Fetched, Has.Count.EqualTo(HardwareRows * HardwareColumns).And.All.Not.Null);
			Assert.That(Text(provider.Fetched[0]!), Does.Contain("Hello"));
			Assert.That(paintedAfterOpen, Is.Zero, "a picture already fetched is not raised again");
			Assert.That(provider.Painted.Select(image => (image.Row, image.Column, image.Revision)),
				Is.EqualTo(new[] { (0, 0, 2L) }));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_device_offline_at_open_answers_for_every_key_and_is_pushed_nothing_until_it_is_back()
	{
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "Hello");
		var deviceId = _fixture.AddDevice("DeckA", layoutSnapshot: Snapshot());
		_fixture.Presence.Set(deviceId, false);
		await _fixture.Service.OpenAsync(deviceId, _fixture.Provider.ProviderId, "DeckA").WaitAsync(_poll);

		var images = await Task.WhenAll(Cells()
				.Select(cell => _fixture.Service.GetKeyImageAsync(deviceId, cell.Row, cell.Column)))
			.WaitAsync(_poll);
		_fixture.UiBroker.PublishHostSnapshot("session-1", Tree(2, "Later"), 2);
		await KeyShowsAsync(deviceId, 0, 0, "Later");
		await _fixture.SettleAsync();
		await QuietAsync();
		var whileOffline = _fixture.Provider.Sequence.Count;

		await _fixture.Service.SetPresenceAsync(deviceId, true);
		await SettleUntilAsync(() => Batches(deviceId).Count > 0, "Nothing was pushed when the device came online.");

		var first = Batches(deviceId)[0];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(images, Has.All.Not.Null);
			Assert.That(Text(images[0]!), Does.Contain("Hello"));
			Assert.That(whileOffline, Is.Zero, "nothing is pushed to a device that is offline");
			Assert.That(_fixture.Provider.Sequence.Take(2), Is.EqualTo(new[] { "surface", "keys" }));
			Assert.That(first, Has.Count.EqualTo(HardwareRows * HardwareColumns));
			Assert.That(Text(Cell(first, 0, 0)), Does.Contain("Later"));
			await Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_in_process_provider_that_fetched_every_key_while_offline_gets_every_key_again_when_it_is_back()
	{
		_fixture.Dispose();
		var provider = new FetchingDeviceProvider();
		_fixture = new DeviceSurfaceFixture(provider: fixture => new InProcessDeviceSurfaceProvider("test-provider",
			provider,
			() => fixture.Service,
			Serilog.Core.Logger.None));
		_fixture.Home.Widgets.Add(DeviceSurfaceFixture.Button("w1", 0, 0));
		_fixture.UiBroker.HostTrees["session-1"] = Tree(1, "Hello");
		var deviceId = _fixture.AddDevice("DeckA", layoutSnapshot: Snapshot());
		_fixture.Presence.Set(deviceId, false);
		await _fixture.Service.OpenAsync(deviceId, "test-provider", "DeckA").WaitAsync(_poll);
		await QuietAsync();
		var paintedWhileOffline = provider.Painted.Count;

		await _fixture.Service.SetPresenceAsync(deviceId, true);
		await SettleUntilAsync(() => provider.Painted.Count >= HardwareRows * HardwareColumns,
			"Not every key arrived when the device came back.");

		var fetched = provider.Fetched.ToDictionary(image => (image!.Row, image.Column), image => image!.Revision);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(fetched, Has.Count.EqualTo(HardwareRows * HardwareColumns));
			Assert.That(paintedWhileOffline, Is.Zero, "nothing is pushed to a device that is offline");
			Assert.That(provider.Painted.Select(image => (image.Row, image.Column)), Is.EquivalentTo(fetched.Keys));
			Assert.That(provider.Painted.Where(image => image.Revision <= fetched[(image.Row, image.Column)]),
				Is.Empty,
				"every key comes back newer than the picture fetched for it");
			await Task.CompletedTask;
		});
	}

	private sealed class FetchingDeviceProvider : IDeviceProvider
	{
		private readonly Lock _gate = new();
		private readonly List<DeviceKeyImage> _painted = [];

		public List<DeviceKeyImage?> Fetched { get; } = [];

		public IReadOnlyList<DeviceKeyImage> Painted
		{
			get
			{
				lock (_gate)
				{
					return [.. _painted];
				}
			}
		}

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public async Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			session.KeyImageChanged += (_, e) =>
			{
				lock (_gate)
				{
					_painted.Add(e.Image);
				}
			};
			foreach (var (row, column) in Cells())
			{
				Fetched.Add(await session.GetKeyImageAsync(row, column, cancellationToken)
					.WaitAsync(_poll,
						cancellationToken));
			}
		}
	}
}
