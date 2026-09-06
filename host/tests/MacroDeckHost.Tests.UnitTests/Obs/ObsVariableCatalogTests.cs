using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsVariableCatalogTests
{
	[Test]
	public async Task Every_discovered_resource_carries_a_legal_id()
	{
		var longName = new string('a', 300);
		var client = new FakeObsClient
		{
			IsConnected = true,
			InputNames = ["Mic Aux", "Desktop Audio", "Main Camera (4K)", "Scene::Main", "Ünter Ström", longName]
		};
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var all = await DiscoverTreeAsync(provider, pageSize: 3);

		Assert.Multiple(() =>
		{
			foreach (var definition in all)
			{
				Assert.That(MacroDeckId.IsValidLocalId(definition.Id, LocalIdKind.Resource), Is.True, definition.Id);
			}

			// A name whose encoded id would exceed the limit must be dropped from discovery outright, never
			// emitted with an over-length (or otherwise truncated, and so unresolvable) id: BuildPage's own
			// MacroDeckId.IsValidLocalId check is what enforces that, and asserting it indirectly through an
			// "if it was discovered" guard would never actually run, since it never is discovered.
			Assert.That(all.Any(d => d.DisplayName.Literal == longName),
				Is.False,
				"a name whose id would exceed the limit must never be discovered, not even with a truncated id");

			var sceneMainInput = all.Single(d => d.DisplayName.Literal == "Scene::Main");
			Assert.That(sceneMainInput.Id, Does.Not.Contain("::"));
		});
	}

	[Test]
	public async Task Whitespace_distinct_names_stay_distinct()
	{
		var client = new FakeObsClient
		{
			IsConnected = true,
			InputNames = ["Mic Aux", "Mic_Aux", "Mic  Aux"]
		};
		client.InputVolumes["Mic Aux"] = 0.10f;
		client.InputVolumes["Mic_Aux"] = 0.50f;
		client.InputVolumes["Mic  Aux"] = 0.90f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var idSpaced = await InputLeafIdAsync(provider, runtime.Id, "Mic Aux", "volume");
		var idUnderscored = await InputLeafIdAsync(provider, runtime.Id, "Mic_Aux", "volume");
		var idDoubleSpaced = await InputLeafIdAsync(provider, runtime.Id, "Mic  Aux", "volume");

		Assert.That(new[] { idSpaced, idUnderscored, idDoubleSpaced }, Is.Unique);

		var valueSpaced = (await provider.ReadAsync(idSpaced)).Value;
		var valueUnderscored = (await provider.ReadAsync(idUnderscored)).Value;
		var valueDoubleSpaced = (await provider.ReadAsync(idDoubleSpaced)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(valueSpaced, Is.EqualTo(-20d));
			Assert.That(valueUnderscored, Is.EqualTo(-6d));
			Assert.That(valueDoubleSpaced, Is.EqualTo(-0.9d));
		});
	}

	// The acceptance criterion for this scenario reads "ResolveAsync (Name `Main Camera`, Type Numeric)".
	// The tree-shape contract is explicit that a leaf's own Name is the field concept ("volume"), not the
	// source name, so this asserts the leaf's Type directly and confirms "Main Camera" by resolving back up
	// to the leaf's container instead of expecting the leaf itself to carry that name.
	[Test]
	public async Task Ids_survive_a_new_provider_instance()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Main Camera"] };
		client.InputVolumes["Main Camera"] = 0.75f;
		var runtime = CreateRuntime(client);
		var firstProvider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(firstProvider, runtime.Id, "Main Camera", "volume");

		var secondProvider = new ObsVariableCatalog(() => [runtime]);
		var resolved = await secondProvider.ResolveAsync(volumeId);
		var container = resolved is null ? null : await secondProvider.ResolveAsync(resolved.ParentId!);
		var value = (await secondProvider.ReadAsync(volumeId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(resolved, Is.Not.Null);
			Assert.That(resolved!.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(container, Is.Not.Null);
			Assert.That(container!.DisplayName.Literal, Is.EqualTo("Main Camera"));
			Assert.That(value, Is.EqualTo(-2.5d));
		});
	}

	[Test]
	public async Task Ids_do_not_repoint_when_the_set_changes()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Main Camera"] };
		client.InputVolumes["Main Camera"] = 0.75f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Main Camera", "volume");

		client.InputNames = ["AAA Camera", "Main Camera"];
		client.InputVolumes["AAA Camera"] = 0.10f;

		var value = (await provider.ReadAsync(volumeId)).Value;

		Assert.That(value, Is.EqualTo(-2.5d));
	}

	[Test]
	public async Task Disconnected_obs_is_resolvable_but_unavailable()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputVolumes["Mic Aux"] = 0.5f;
		client.MutedInputs["Mic Aux"] = true;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Mic Aux", "volume");
		var mutedId = await InputLeafIdAsync(provider, runtime.Id, "Mic Aux", "muted");

		client.IsConnected = false;

		var resolvedVolume = await provider.ResolveAsync(volumeId);
		var volumeValue = (await provider.ReadAsync(volumeId)).Value;
		var muteValue = (await provider.ReadAsync(mutedId)).Value;
		var discovered = await provider.DiscoverAsync(new VariableCatalogQuery { ParentId = runtime.Id.ToString("D") });

		Assert.Multiple(() =>
		{
			Assert.That(resolvedVolume, Is.Not.Null);
			Assert.That(resolvedVolume!.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(volumeValue, Is.Null);
			Assert.That(muteValue, Is.Null);
			Assert.That(discovered.Items, Is.Empty);
		});
	}

	[Test]
	public async Task Invalid_ids_are_rejected()
	{
		var provider = new ObsVariableCatalog(() => []);

		var empty = await provider.ResolveAsync("");
		var notObsResource = await provider.ResolveAsync("not-an-obs-resource");
		var unknownKind = await provider.ResolveAsync($"{Guid.NewGuid():D}/widget/x");

		Assert.Multiple(() =>
		{
			Assert.That(empty, Is.Null);
			Assert.That(notObsResource, Is.Null);
			Assert.That(unknownKind, Is.Null);
		});
	}

	// ObsConnection coalesces per-target reads for one second (see ObsConnection.CachedTargetReadAsync), so
	// the "recreated" read has to happen after that window - otherwise it would just answer from the same
	// cached read that already observed the input gone.
	[Test]
	public async Task A_deleted_source_keeps_resolving_and_resumes()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputVolumes["Mic Aux"] = 0.10f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Mic Aux", "volume");

		client.DeletedNames.Add("Mic Aux");

		var resolvedAfterDelete = await provider.ResolveAsync(volumeId);
		var valueAfterDelete = (await provider.ReadAsync(volumeId)).Value;

		await Task.Delay(TimeSpan.FromMilliseconds(1100));

		client.DeletedNames.Remove("Mic Aux");
		client.InputVolumes["Mic Aux"] = 0.30f;

		var valueAfterRecreate = (await provider.ReadAsync(volumeId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(resolvedAfterDelete, Is.Not.Null);
			Assert.That(valueAfterDelete, Is.Null);
			Assert.That(valueAfterRecreate, Is.EqualTo(-10.5d));
		});
	}

	[Test]
	public async Task String_true_is_not_boolean_true_in_settings()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputSettingsJson["Mic Aux"] =
			"""{"shutdown": true, "css_flag": "true", "fps": 30, "url_count": "75", "reroute_audio": false}""";
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var containerId = await InputContainerIdAsync(provider, runtime.Id, "Mic Aux");
		var leaves = await DiscoverAllAsync(provider, containerId);

		var shutdown = leaves.Single(l => l.DisplayName.Literal == "shutdown");
		var cssFlag = leaves.Single(l => l.DisplayName.Literal == "css_flag");
		var fps = leaves.Single(l => l.DisplayName.Literal == "fps");
		var urlCount = leaves.Single(l => l.DisplayName.Literal == "url_count");
		var rerouteAudio = leaves.Single(l => l.DisplayName.Literal == "reroute_audio");

		var shutdownValue = (await provider.ReadAsync(shutdown.Id!)).Value;
		var cssFlagValue = (await provider.ReadAsync(cssFlag.Id!)).Value;
		var fpsValue = (await provider.ReadAsync(fps.Id!)).Value;
		var urlCountValue = (await provider.ReadAsync(urlCount.Id!)).Value;
		var rerouteAudioValue = (await provider.ReadAsync(rerouteAudio.Id!)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(shutdown.Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(shutdownValue, Is.TypeOf<bool>());
			Assert.That(shutdownValue, Is.EqualTo(true));

			Assert.That(cssFlag.Type, Is.EqualTo(VariableType.Text));
			Assert.That(cssFlagValue, Is.TypeOf<string>());
			Assert.That(cssFlagValue, Is.EqualTo("true"));

			Assert.That(fps.Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(fpsValue, Is.EqualTo(30d));

			Assert.That(urlCount.Type, Is.EqualTo(VariableType.Text));
			Assert.That(urlCountValue, Is.TypeOf<string>());
			Assert.That(urlCountValue, Is.EqualTo("75"));

			Assert.That(rerouteAudio.Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(rerouteAudioValue, Is.TypeOf<bool>());
			Assert.That(rerouteAudioValue, Is.EqualTo(false));
		});
	}

	[Test]
	public async Task Nonscalar_settings_never_leak_a_json_element()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputSettingsJson["Mic Aux"]
			= """{"font": {"face": "Arial", "size": 12}, "tags": ["a", "b"], "opt": null}""";
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var containerId = await InputContainerIdAsync(provider, runtime.Id, "Mic Aux");

		List<VariableDefinition> leaves = [];
		Assert.DoesNotThrowAsync(async () => leaves = await DiscoverAllAsync(provider, containerId));

		var settingLeaves = leaves.Where(l => l.DisplayName.Literal is "font" or "tags" or "opt").ToList();
		Assert.That(settingLeaves, Has.Count.EqualTo(3));

		foreach (var leaf in settingLeaves)
		{
			var value = (await provider.ReadAsync(leaf.Id!)).Value;
			Assert.That(value is null or string or double or bool,
				Is.True,
				$"{leaf.DisplayName.Literal} => {value?.GetType()}");
		}
	}

	[Test]
	public async Task A_number_too_large_to_represent_falls_back_to_text_and_never_reads_as_zero()
	{
		// JSON numbers are arbitrary-precision; 1e999 is syntactically legal but overflows a double, so
		// TryGetDouble reports failure for it. The dynamic-variable contract forbids substituting 0/false/""
		// for a value that could not actually be read.
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputSettingsJson["Mic Aux"] = """{"overflow": 1e999}""";
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var containerId = await InputContainerIdAsync(provider, runtime.Id, "Mic Aux");
		var leaves = await DiscoverAllAsync(provider, containerId);
		var overflow = leaves.Single(l => l.DisplayName.Literal == "overflow");

		var value = (await provider.ReadAsync(overflow.Id!)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(overflow.Type,
				Is.EqualTo(VariableType.Text),
				"a number TryGetDouble cannot represent must fall back to Text, never silently become Numeric 0");
			Assert.That(value, Is.TypeOf<string>());
			Assert.That(value, Is.EqualTo("1e999"), "the raw JSON text must survive rather than a substituted zero");
			Assert.That(value, Is.Not.EqualTo(0d));
		});
	}

	[Test]
	public async Task Volume_is_reported_on_the_decibel_scale_obs_itself_shows()
	{
		// OBS's protocol carries a linear multiplier, but its own audio mixer is a -60..0 dB scale, and
		// that is the number the user reads off OBS and expects to match. 0.13 is the multiplier behind
		// the -17.7 dB OBS displays; a reading in percent would say 13 instead.
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Desktop Audio"] };
		client.InputVolumes["Desktop Audio"] = 0.13f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Desktop Audio", "volume");
		var reading = await provider.ReadAsync(volumeId);

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo(-17.7d));
			Assert.That(reading.Min, Is.EqualTo(-60d));
			Assert.That(reading.Step, Is.EqualTo(0.1d));
		});
	}

	[Test]
	public async Task Writing_decibels_sets_the_multiplier_obs_expects()
	{
		// The scale is only half the contract: OBS is still told a multiplier, so a write has to convert
		// back. Reading what was just written must return it unchanged, or a slider would jump under the
		// finger on the next poll.
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Desktop Audio"] };
		client.InputVolumes["Desktop Audio"] = 1.0f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Desktop Audio", "volume");
		var result = await provider.SetValueAsync(volumeId, -17.7d);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(client.InputVolumes["Desktop Audio"], Is.EqualTo(0.13f).Within(0.001f));
		});

		Assert.That((await provider.ReadAsync(volumeId)).Value, Is.EqualTo(-17.7d));
	}

	[Test]
	public async Task Volume_above_zero_decibels_is_reported_not_clamped()
	{
		// An input gain-boosted in Advanced Audio Properties sits above 0 dB. Reporting a ceiling of 0
		// would make a slider that displays the boost unable to hold it: the first drag would cut it.
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Boosted Mic"] };
		client.InputVolumes["Boosted Mic"] = 1.6f;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var volumeId = await InputLeafIdAsync(provider, runtime.Id, "Boosted Mic", "volume");
		var reading = await provider.ReadAsync(volumeId);

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo(4.1d));
			Assert.That(reading.Max, Is.EqualTo(4.1d));
		});
	}

	[Test]
	public async Task Scene_item_visibility_is_per_scene()
	{
		var client = new FakeObsClient
		{
			IsConnected = true,
			SceneNames = ["Gameplay", "Just Chatting"]
		};
		client.SceneItems["Gameplay"] = ["Webcam"];
		client.SceneItems["Just Chatting"] = ["Webcam"];
		client.VisibleSources["Gameplay:Webcam"] = true;
		client.VisibleSources["Just Chatting:Webcam"] = false;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var gameplayItemId = await SceneItemLeafIdAsync(provider, runtime.Id, "Gameplay", "Webcam");
		var chattingItemId = await SceneItemLeafIdAsync(provider, runtime.Id, "Just Chatting", "Webcam");

		Assert.That(gameplayItemId, Is.Not.EqualTo(chattingItemId));

		var gameplayValue = (await provider.ReadAsync(gameplayItemId)).Value;
		var chattingValue = (await provider.ReadAsync(chattingItemId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(gameplayValue, Is.TypeOf<bool>());
			Assert.That(gameplayValue, Is.EqualTo(true));
			Assert.That(chattingValue, Is.TypeOf<bool>());
			Assert.That(chattingValue, Is.EqualTo(false));
		});
	}

	[Test]
	public async Task Active_and_showing_differ()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Preview Only Cam"] };
		client.SourceActivity["Preview Only Cam"] = new ObsSourceActivity(false, true);
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var activeId = await InputLeafIdAsync(provider, runtime.Id, "Preview Only Cam", "active");
		var showingId = await InputLeafIdAsync(provider, runtime.Id, "Preview Only Cam", "showing");

		var activeValue = (await provider.ReadAsync(activeId)).Value;
		var showingValue = (await provider.ReadAsync(showingId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(activeValue, Is.EqualTo(false));
			Assert.That(showingValue, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task The_six_audio_tracks_are_individual()
	{
		var client = new FakeObsClient { IsConnected = true, InputNames = ["Mic Aux"] };
		client.InputAudioTracksByName["Mic Aux"] = new ObsAudioTracks([true, false, true, false, false, false]);
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var values = new List<object?>();
		for (var track = 1; track <= 6; track++)
		{
			var id = await InputLeafIdAsync(provider, runtime.Id, "Mic Aux", $"track-{track}");
			values.Add((await provider.ReadAsync(id)).Value);
		}

		Assert.That(values, Is.EqualTo(new object?[] { true, false, true, false, false, false }));
	}

	[Test]
	public async Task A_negative_sync_offset_survives()
	{
		var client = new FakeObsClient
		{
			IsConnected = true,
			InputNames = ["Delayed Mic", "OnTime Mic"]
		};
		client.InputAudioSyncOffsetsMs["Delayed Mic"] = -250;
		client.InputAudioSyncOffsetsMs["OnTime Mic"] = 0;
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var delayedId = await InputLeafIdAsync(provider, runtime.Id, "Delayed Mic", "sync-offset");
		var onTimeId = await InputLeafIdAsync(provider, runtime.Id, "OnTime Mic", "sync-offset");

		var delayedValue = (await provider.ReadAsync(delayedId)).Value;
		var onTimeValue = (await provider.ReadAsync(onTimeId)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(delayedValue, Is.EqualTo(-250));
			Assert.That(onTimeValue, Is.EqualTo(0));
			Assert.That(onTimeValue, Is.Not.Null);
		});
	}

	[Test]
	public async Task Paging_is_exhaustive_and_terminating()
	{
		var names = Enumerable.Range(0, 500).Select(i => $"Input {i:D4}").ToList();
		var client = new FakeObsClient { IsConnected = true, InputNames = names };
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var seen = new List<string>();
		string? token = null;
		var pageCount = 0;
		do
		{
			var page = await provider.DiscoverAsync(new VariableCatalogQuery
			{
				ParentId = runtime.Id.ToString("D"),
				ContinuationToken = token,
				PageSize = 100
			});

			Assert.That(page.Items.Count, Is.LessThanOrEqualTo(100));
			seen.AddRange(page.Items.Select(item => item.Id!));
			token = page.ContinuationToken;
			pageCount++;
			Assert.That(pageCount, Is.LessThan(20), "paging did not terminate");
		} while (token is not null);

		Assert.Multiple(() =>
		{
			Assert.That(seen, Has.Count.EqualTo(500));
			Assert.That(seen.Distinct().Count(), Is.EqualTo(500));
			Assert.That(token, Is.Null);
		});
	}

	[Test]
	public async Task Mid_browse_mutation_does_not_duplicate()
	{
		var names = Enumerable.Range(0, 150).Select(i => $"Input {i:D4}").ToList();
		var client = new FakeObsClient { IsConnected = true, InputNames = names };
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var page1 = await provider.DiscoverAsync(new VariableCatalogQuery
		{
			ParentId = runtime.Id.ToString("D"),
			PageSize = 100
		});

		Assert.That(page1.ContinuationToken, Is.Not.Null);

		var mutated = names.ToList();
		mutated.Remove("Input 0140");
		mutated.Add("Input New");
		client.InputNames = mutated;

		var laterIds = new List<string>();
		var token = page1.ContinuationToken;
		do
		{
			var page = await provider.DiscoverAsync(new VariableCatalogQuery
			{
				ParentId = runtime.Id.ToString("D"),
				ContinuationToken = token,
				PageSize = 100
			});
			laterIds.AddRange(page.Items.Select(item => item.Id!));
			token = page.ContinuationToken;
		} while (token is not null);

		var page1Ids = page1.Items.Select(item => item.Id).ToHashSet();
		Assert.That(laterIds.Any(id => page1Ids.Contains(id)), Is.False);
	}

	[Test]
	public async Task Every_parent_id_resolves_and_containers_are_not_bindable()
	{
		var client = new FakeObsClient
		{
			IsConnected = true,
			InputNames = ["Mic Aux"],
			SceneNames = ["Gameplay"]
		};
		client.SceneItems["Gameplay"] = ["Webcam"];
		client.SourceFilters["Mic Aux"] = ["Noise Suppress"];
		client.SourceFilters["Gameplay"] = ["Color Correction"];
		client.InputSettingsJson["Mic Aux"] = """{"gain": 3}""";
		var runtime = CreateRuntime(client);
		var provider = new ObsVariableCatalog(() => [runtime]);

		var all = await DiscoverTreeAsync(provider, pageSize: 3);

		var parentIds = all.Where(d => d.ParentId is not null).Select(d => d.ParentId!).Distinct().ToList();
		var resolvedParents = new List<(string ParentId, VariableDefinition? Definition)>();
		foreach (var parentId in parentIds)
		{
			resolvedParents.Add((parentId, await provider.ResolveAsync(parentId)));
		}

		Assert.Multiple(() =>
		{
			foreach (var (parentId, definition) in resolvedParents)
			{
				Assert.That(definition, Is.Not.Null, parentId);
			}

			foreach (var definition in all.Where(d => d.IsContainer))
			{
				Assert.That(definition.IsBindable, Is.False, definition.Id);
			}
		});
	}

	[Test]
	public async Task Multi_connection_isolation()
	{
		var clientA = new FakeObsClient { IsConnected = true, InputNames = ["Mic"] };
		clientA.InputVolumes["Mic"] = 0.20f;
		var clientB = new FakeObsClient { IsConnected = true, InputNames = ["Mic"] };
		clientB.InputVolumes["Mic"] = 0.80f;

		var runtimeA = CreateRuntime(clientA, "Connection A");
		var runtimeB = CreateRuntime(clientB, "Connection B");
		var provider = new ObsVariableCatalog(() => [runtimeA, runtimeB]);

		var idFromA = await InputLeafIdAsync(provider, runtimeA.Id, "Mic", "volume");
		var idFromB = await InputLeafIdAsync(provider, runtimeB.Id, "Mic", "volume");

		Assert.That(idFromA, Is.Not.EqualTo(idFromB));

		var valueFromA = (await provider.ReadAsync(idFromA)).Value;

		Assert.That(valueFromA, Is.EqualTo(-14d));
	}

	private static ObsRuntime CreateRuntime(FakeObsClient client, string title = "OBS", Guid? id = null)
		=> new(id ?? Guid.NewGuid(),
			title,
			new ObsConfigurationIdentity(title, "key"),
			new ObsConfigurationSettings("localhost", 4455, null),
			new ObsConnection(client, "ws://localhost:4455", null));

	private static async Task<List<VariableDefinition>> DiscoverAllAsync(
		ObsVariableCatalog provider,
		string? parentId,
		int pageSize = 100)
	{
		var results = new List<VariableDefinition>();
		string? token = null;
		do
		{
			var page = await provider.DiscoverAsync(new VariableCatalogQuery
			{
				ParentId = parentId,
				ContinuationToken = token,
				PageSize = pageSize
			});
			results.AddRange(page.Items);
			token = page.ContinuationToken;
		} while (token is not null);

		return results;
	}

	private static async Task<List<VariableDefinition>> DiscoverTreeAsync(ObsVariableCatalog provider,
		int pageSize = 100)
	{
		var all = new List<VariableDefinition>();
		var queue = new Queue<string?>();
		queue.Enqueue(null);

		while (queue.Count > 0)
		{
			var parentId = queue.Dequeue();
			var items = await DiscoverAllAsync(provider, parentId, pageSize);
			all.AddRange(items);
			foreach (var item in items.Where(item => item.IsContainer))
			{
				queue.Enqueue(item.Id);
			}
		}

		return all;
	}

	private static async Task<string> InputContainerIdAsync(ObsVariableCatalog provider,
		Guid runtimeId,
		string inputName)
	{
		var containers = await DiscoverAllAsync(provider, runtimeId.ToString("D"));
		return containers.Single(c => c.DisplayName.Literal == inputName).Id!;
	}

	private static async Task<string> SceneContainerIdAsync(ObsVariableCatalog provider,
		Guid runtimeId,
		string sceneName)
	{
		var containers = await DiscoverAllAsync(provider, runtimeId.ToString("D"));
		return containers.Single(c => c.DisplayName.Literal == sceneName).Id!;
	}

	private static async Task<string> InputLeafIdAsync(
		ObsVariableCatalog provider,
		Guid runtimeId,
		string inputName,
		string leafName)
	{
		var containerId = await InputContainerIdAsync(provider, runtimeId, inputName);
		var leaves = await DiscoverAllAsync(provider, containerId);
		return leaves.Single(leaf => leaf.DisplayName.Literal == leafName).Id!;
	}

	private static async Task<string> SceneItemLeafIdAsync(
		ObsVariableCatalog provider,
		Guid runtimeId,
		string sceneName,
		string itemName)
	{
		var sceneId = await SceneContainerIdAsync(provider, runtimeId, sceneName);
		var items = await DiscoverAllAsync(provider, sceneId);
		var itemContainer = items.Single(item => item.DisplayName.Literal == itemName);
		var leaves = await DiscoverAllAsync(provider, itemContainer.Id!);
		return leaves.Single(leaf => leaf.DisplayName.Literal == "visible").Id!;
	}
}
