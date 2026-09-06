using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>Round-trips every DTO added for the <c>music-player</c>, <c>weather</c> and
/// <c>virtual-profiles</c> capability kinds through the wire serializer, in the style of
/// <see cref="CapabilityDtoSerializationTests" />.</summary>
[TestFixture]
public class MusicPlayerWeatherVirtualProfilesDtoSerializationTests
{
	private static readonly string[] _singleArtist = ["Artist"];

	[Test]
	public void Music_player_describe_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new MusicPlayerDescribePayload
		{
			ProviderName = "Spotify",
			Instances =
			[
				new MusicPlayerInstanceDto
				{
					Id = "account-1", DisplayName = "Spotify (alice)", HasCatalog = true, HasDevices = true
				}
			]
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<MusicPlayerDescribePayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.ProviderName, Is.EqualTo("Spotify"));
			Assert.That(actual.Instances, Has.Count.EqualTo(1));
			Assert.That(actual.Instances[0].HasCatalog, Is.True);
			Assert.That(actual.Instances[0].HasDevices, Is.True);
		});
	}

	[Test]
	public void Music_player_state_dto_round_trips_and_tolerates_unknown_fields()
	{
		var dto = new MusicPlayerStateDto
		{
			IsConnected = true,
			PlaybackState = "Playing",
			TrackName = "Song",
			Artists = ["Artist"],
			AlbumName = "Album",
			ArtworkId = "art-1",
			PositionSeconds = 12.5,
			DurationSeconds = 210,
			VolumePercent = 80,
			ShuffleEnabled = true,
			RepeatMode = "Context",
			DeviceName = "Living Room",
			DeviceType = "Speaker"
		};

		var json = JsonSerializer.Serialize(dto, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<MusicPlayerStateDto>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.PlaybackState, Is.EqualTo("Playing"));
			Assert.That(actual.RepeatMode, Is.EqualTo("Context"));
			Assert.That(actual.Artists, Is.EqualTo(_singleArtist));
			Assert.That(actual.VolumePercent, Is.EqualTo(80));
		});
	}

	[Test]
	public void Music_player_artwork_arguments_and_result_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new MusicPlayerArtworkArguments { InstanceId = "account-1", ArtworkId = "art-1" };
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments =
			JsonSerializer.Deserialize<MusicPlayerArtworkArguments>(InjectUnknownField(argumentsJson),
				PluginProtocolJson.Options);
		Assert.That(actualArguments!.ArtworkId, Is.EqualTo("art-1"));

		var result = new MusicPlayerArtworkResult { Data = Convert.ToBase64String([1, 2, 3]), MimeType = "image/png" };
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult =
			JsonSerializer.Deserialize<MusicPlayerArtworkResult>(InjectUnknownField(resultJson),
				PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actualResult!.Data, Is.EqualTo(result.Data));
			Assert.That(actualResult.MimeType, Is.EqualTo("image/png"));
		});
	}

	[Test]
	public void Music_player_play_item_arguments_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new MusicPlayerPlayItemArguments
		{
			InstanceId = "account-1",
			Item = new MusicPlayerCatalogItemDto
			{
				Id = "track-1", Title = "Song", Kind = "Track", Subtitle = "Artist", ArtworkId = "art-1",
				DurationSeconds = 180
			}
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<MusicPlayerPlayItemArguments>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.InstanceId, Is.EqualTo("account-1"));
			Assert.That(actual.Item.Kind, Is.EqualTo("Track"));
			Assert.That(actual.Item.DurationSeconds, Is.EqualTo(180));
		});
	}

	[Test]
	public void Music_player_catalog_and_devices_round_trip_and_tolerate_unknown_fields()
	{
		var catalogArguments = new MusicPlayerCatalogArguments
			{ InstanceId = "account-1", Kind = "Playlist", Filter = "liv" };
		var catalogArgumentsJson = JsonSerializer.Serialize(catalogArguments, PluginProtocolJson.Options);
		var actualCatalogArguments = JsonSerializer.Deserialize<MusicPlayerCatalogArguments>(
			InjectUnknownField(catalogArgumentsJson),
			PluginProtocolJson.Options);
		Assert.That(actualCatalogArguments!.Kind, Is.EqualTo("Playlist"));

		var catalogResult = new MusicPlayerCatalogResult
		{
			Items = [new MusicPlayerCatalogItemDto { Id = "p1", Title = "Playlist", Kind = "Playlist" }]
		};
		var catalogResultJson = JsonSerializer.Serialize(catalogResult, PluginProtocolJson.Options);
		var actualCatalogResult =
			JsonSerializer.Deserialize<MusicPlayerCatalogResult>(InjectUnknownField(catalogResultJson),
				PluginProtocolJson.Options);
		Assert.That(actualCatalogResult!.Items, Has.Count.EqualTo(1));

		var devicesResult = new MusicPlayerDevicesResult
		{
			Devices =
			[
				new MusicPlayerDeviceDto
					{ Id = "d1", Name = "Speaker", Type = "Speaker", IsActive = true, VolumePercent = 50 }
			]
		};
		var devicesResultJson = JsonSerializer.Serialize(devicesResult, PluginProtocolJson.Options);
		var actualDevicesResult =
			JsonSerializer.Deserialize<MusicPlayerDevicesResult>(InjectUnknownField(devicesResultJson),
				PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(actualDevicesResult!.Devices, Has.Count.EqualTo(1));
			Assert.That(actualDevicesResult.Devices[0].IsActive, Is.True);
		});

		var transferArguments = new MusicPlayerTransferArguments
			{ InstanceId = "account-1", DeviceId = "d1", StartPlayback = true };
		var transferJson = JsonSerializer.Serialize(transferArguments, PluginProtocolJson.Options);
		var actualTransfer =
			JsonSerializer.Deserialize<MusicPlayerTransferArguments>(InjectUnknownField(transferJson),
				PluginProtocolJson.Options);
		Assert.That(actualTransfer!.StartPlayback, Is.True);
	}

	[Test]
	public void Music_player_seek_volume_shuffle_and_repeat_arguments_round_trip()
	{
		AssertRoundTrips(new MusicPlayerSeekArguments { InstanceId = "a", PositionSeconds = 5.5 });
		AssertRoundTrips(new MusicPlayerVolumeArguments { InstanceId = "a", VolumePercent = 42 });
		AssertRoundTrips(new MusicPlayerShuffleArguments { InstanceId = "a", Enabled = true });
		AssertRoundTrips(new MusicPlayerRepeatArguments { InstanceId = "a", Mode = "Track" });
		return;

		static void AssertRoundTrips<T>(T value)
		{
			var json = JsonSerializer.Serialize(value, PluginProtocolJson.Options);
			var actual = JsonSerializer.Deserialize<T>(json, PluginProtocolJson.Options);
			Assert.That(actual, Is.EqualTo(value));
		}
	}

	[Test]
	public void Weather_describe_payload_and_snapshot_round_trip_and_tolerate_unknown_fields()
	{
		var payload = new WeatherDescribePayload
		{
			ProviderName = "Open-Meteo",
			Instances = [new WeatherStationInstanceDto { Id = "berlin", DisplayName = "Berlin, Germany" }]
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<WeatherDescribePayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.ProviderName, Is.EqualTo("Open-Meteo"));
			Assert.That(actual.Instances.Single().DisplayName, Is.EqualTo("Berlin, Germany"));
		});

		var snapshot = new WeatherSnapshotDto
		{
			IsAvailable = true,
			LocationName = "Berlin, Germany",
			Temperature = 18.5,
			ApparentTemperature = 17.0,
			Condition = "PartlyCloudy",
			IsDay = true,
			Unit = "Celsius",
			Days =
			[
				new WeatherForecastDayDto { Date = new DateOnly(2026, 8, 11), Condition = "Rain", Min = 10, Max = 20 }
			]
		};

		var snapshotJson = JsonSerializer.Serialize(snapshot, PluginProtocolJson.Options);
		var actualSnapshot =
			JsonSerializer.Deserialize<WeatherSnapshotDto>(InjectUnknownField(snapshotJson),
				PluginProtocolJson.Options);

		Assert.That(actualSnapshot, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actualSnapshot!.Condition, Is.EqualTo("PartlyCloudy"));
			Assert.That(actualSnapshot.Unit, Is.EqualTo("Celsius"));
			Assert.That(actualSnapshot.Days.Single().Condition, Is.EqualTo("Rain"));
			Assert.That(actualSnapshot.Days.Single().Date, Is.EqualTo(new DateOnly(2026, 8, 11)));
		});
	}

	[Test]
	public void Weather_instance_arguments_and_instances_result_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new WeatherInstanceArguments { InstanceId = "berlin" };
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments =
			JsonSerializer.Deserialize<WeatherInstanceArguments>(InjectUnknownField(argumentsJson),
				PluginProtocolJson.Options);
		Assert.That(actualArguments!.InstanceId, Is.EqualTo("berlin"));

		var result = new WeatherInstancesResult
		{
			Instances = [new WeatherStationInstanceDto { Id = "berlin", DisplayName = "Berlin, Germany" }]
		};
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult =
			JsonSerializer.Deserialize<WeatherInstancesResult>(InjectUnknownField(resultJson),
				PluginProtocolJson.Options);
		Assert.That(actualResult!.Instances, Has.Count.EqualTo(1));
	}

	[Test]
	public void Virtual_profiles_describe_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new VirtualProfilesDescribePayload
		{
			ProviderName = "Spotify",
			Profiles =
			[
				new VirtualProfileDescriptorDto
				{
					Id = "car-thing",
					Name = "Car Thing",
					Layout = new ProfileLayoutDto
						{ Kind = "Grid", Rows = 2, Columns = 4, RowsLocked = true, ColumnsLocked = true },
					Folders =
					[
						new VirtualFolderDescriptorDto
						{
							Id = "main",
							Name = "Main",
							Order = 0,
							Widgets =
							[
								new VirtualWidgetDescriptorDto
								{
									Id = "play", Type = "ActionButton", PositionX = 0, PositionY = 0, Width = 1,
									Height = 1
								}
							]
						}
					]
				}
			]
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual =
			JsonSerializer.Deserialize<VirtualProfilesDescribePayload>(InjectUnknownField(json),
				PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.ProviderName, Is.EqualTo("Spotify"));
			Assert.That(actual.Profiles.Single().Layout.Kind, Is.EqualTo("Grid"));
			Assert.That(actual.Profiles.Single().Folders.Single().Widgets.Single().Type, Is.EqualTo("ActionButton"));
		});
	}

	[Test]
	public void Virtual_profiles_result_and_widget_interaction_arguments_round_trip_and_tolerate_unknown_fields()
	{
		var result = new VirtualProfilesResult
		{
			Profiles =
			[
				new VirtualProfileDescriptorDto
				{
					Id = "p1",
					Name = "P1",
					Layout = new ProfileLayoutDto { Kind = "Grid", Rows = 1, Columns = 1 },
					Folders = []
				}
			]
		};
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult =
			JsonSerializer.Deserialize<VirtualProfilesResult>(InjectUnknownField(resultJson),
				PluginProtocolJson.Options);
		Assert.That(actualResult!.Profiles, Has.Count.EqualTo(1));

		var arguments = new WidgetInteractionArguments
		{
			ProfileId = string.Empty, FolderId = "main", WidgetId = "play", TriggerType = "press"
		};
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments =
			JsonSerializer.Deserialize<WidgetInteractionArguments>(InjectUnknownField(argumentsJson),
				PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actualArguments!.ProfileId, Is.EqualTo(string.Empty));
			Assert.That(actualArguments.FolderId, Is.EqualTo("main"));
			Assert.That(actualArguments.WidgetId, Is.EqualTo("play"));
			Assert.That(actualArguments.TriggerType, Is.EqualTo("press"));
		});
	}

	/// <summary>Same guard as <see cref="CapabilityDtoSerializationTests" />'s, extended to the three
	/// new namespaces: nothing here may carry an enum-typed property, since <see cref="PluginProtocolJson.Options" />
	/// has no <see cref="System.Text.Json.Serialization.JsonStringEnumConverter" />.</summary>
	[Test]
	public void No_dto_in_music_player_weather_or_virtual_profiles_exposes_an_enum_typed_property()
	{
		var candidateTypes = typeof(MusicPlayerInstanceDto).Assembly.GetTypes()
			.Where(type =>
				type.IsPublic &&
				type.Namespace is not null &&
				(type.Namespace == typeof(MusicPlayerInstanceDto).Namespace ||
					type.Namespace == typeof(WeatherStationInstanceDto).Namespace ||
					type.Namespace == typeof(VirtualProfileDescriptorDto).Namespace));

		Assert.Multiple(() =>
		{
			foreach (var type in candidateTypes)
			{
				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				{
					Assert.That(property.PropertyType.IsEnum,
						Is.False,
						$"{type.FullName}.{property.Name} is an enum, which would serialize as an undocumented integer.");
				}
			}
		});
	}

	private static string InjectUnknownField(string json)
	{
		var body = json[..^1];
		var separator = body.EndsWith('{') ? string.Empty : ",";
		return body + separator + "\"unknownField\":\"ignored\"}";
	}
}
