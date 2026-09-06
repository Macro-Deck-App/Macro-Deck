using System.IO.Compression;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Migration.MacroDeck2;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Migration;

[TestFixture]
public class MacroDeck2MigrationSourceTests
{
	private const string MachineKey = "b0864908-0000-4aba-8887-4135dca10000";

	private const string FolderSwitcherType = "SuchByte.MacroDeck.Folders.Plugin.FolderSwitcher, Macro Deck 2";

	private const string GoToParentType = "SuchByte.MacroDeck.Folders.Plugin.GoToParentFolder, Macro Deck 2";

	private const string UnknownActionType = "Some.Vendor.Plugin.Actions.DoThingAction, Some Vendor Plugin";

	private MacroDeck2Fixture _fixture = null!;

	private IMigrationActionRegistry _migrators = new NoMigratorRegistry();

	[SetUp]
	public void SetUp()
	{
		_fixture = new MacroDeck2Fixture();
		_migrators = new NoMigratorRegistry();
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task Read_TranslatesEveryProfileAndParentsFoldersFromTheirChildren()
	{
		_fixture.WithConfig()
			.WithProfile("0",
				"Profil 1",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root", "*Root*", [MacroDeck2Fixture.Button(4, 1)], "sub"),
					MacroDeck2Fixture.Folder("sub", "SinusBot", [MacroDeck2Fixture.Button(0, 0)])
				])
			.WithProfile("second",
				"Profile 2",
				rows: 2,
				columns: 3,
				folders: [MacroDeck2Fixture.Folder("second-root", "*Root*", [MacroDeck2Fixture.Button(0, 0)])])
			.Build();

		var plan = await Read();

		var first = plan.Profiles[0].Profile!;
		var root = first.Folders.Single(folder => folder.Name == "Root");
		var sub = first.Folders.Single(folder => folder.Name == "SinusBot");

		Assert.Multiple(() =>
		{
			Assert.That(plan.Profiles, Has.Count.EqualTo(2));
			Assert.That(first.Name, Is.EqualTo("Profil 1"));
			Assert.That(first.DefaultRows, Is.EqualTo(3));
			Assert.That(first.DefaultColumns, Is.EqualTo(5));
			Assert.That(root.ParentId, Is.Null, "the root folder has no parent");
			Assert.That(sub.ParentId, Is.EqualTo(root.Id), "a child listed in Childs is parented to that folder");
			Assert.That(root.Widgets.Single().PositionX, Is.EqualTo(4));
			Assert.That(root.Widgets.Single().PositionY, Is.EqualTo(1));
			Assert.That(plan.Profiles[1].Profile!.Name, Is.EqualTo("Profile 2"));
		});
	}

	/// <summary>
	/// Macro Deck 2's radius is a percentage of the button height and names the corner arc's diameter, so
	/// its default of 40 drew a corner a fifth of the height across. Macro Deck 3 stores pixels against a
	/// 120px reference cell, where that same look is 24 - carrying the number over unchanged is what made
	/// a migrated deck arrive visibly rounder than it ever was.
	/// </summary>
	[Test]
	public async Task Read_TranslatesTheButtonRadiusIntoMacroDeck3sOwnUnits()
	{
		await SingleButton(MacroDeck2Fixture.Button(0, 0));

		var plan = await Read();

		Assert.That(plan.Profiles[0].Profile!.DefaultWidgetBorderRadius, Is.EqualTo(24));
	}

	// Macro Deck 2 always stored an off and an on face, even when they looked the same. Turning every
	// migrated button into a two-state one would bury the ordinary case in state UI it never needed.
	[Test]
	public async Task Read_WithIdenticalFaces_DoesNotProduceAStateModeButton()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			backColorOff: "Lime",
			backColorOn: "Lime",
			labelOff: MacroDeck2Fixture.Label("Same"),
			labelOn: MacroDeck2Fixture.Label("Same")));

		var data = await SingleWidgetData();

		Assert.Multiple(() =>
		{
			Assert.That(data["stateMode"], Is.Null);
			Assert.That(data["states"], Is.Null);
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Same"));
			Assert.That(data["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#00ff00"));
		});
	}

	/// <summary>
	/// The on face of a Macro Deck 2 button was almost never edited, so it differs from the off face on
	/// nearly every button - and that difference never meant the button had two states. Migrating on that
	/// signal would change behaviour rather than preserve it: Macro Deck 3 advances a state-mode button on
	/// every short press unless a mapping is authoritative, so the face would start flipping on a button
	/// that only ever showed one.
	/// </summary>
	[Test]
	public async Task Read_WithDifferingFacesAndNothingDrivingTheState_KeepsTheOneFaceTheButtonShowed()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			backColorOff: "35, 35, 35",
			backColorOn: "Red",
			labelOff: MacroDeck2Fixture.Label("Off", position: 0),
			labelOn: MacroDeck2Fixture.Label("On", position: 1)));

		var data = await SingleWidgetData();

		Assert.Multiple(() =>
		{
			Assert.That(data["stateMode"], Is.Null);
			Assert.That(data["states"], Is.Null);
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Off"));
			Assert.That(data["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#232323"));
		});
	}

	[Test]
	public async Task Read_WithAButtonThatTogglesItsOwnState_ProducesOffAndOnStates()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			backColorOff: "35, 35, 35",
			backColorOn: "Red",
			labelOff: MacroDeck2Fixture.Label("Off", position: 0),
			labelOn: MacroDeck2Fixture.Label("On", position: 1),
			actions:
			[
				MacroDeck2Fixture.Action("SuchByte.MacroDeck.ActionButton.ActionButtonToggleStateAction, Macro Deck 2")
			]));

		var data = await SingleWidgetData();
		var states = data["states"]!.AsArray();

		Assert.Multiple(() =>
		{
			Assert.That(data["stateMode"]!.GetValue<bool>(), Is.True);
			Assert.That(states, Has.Count.EqualTo(2));
			Assert.That(states[0]!["id"]!.GetValue<string>(), Is.EqualTo("off"));
			Assert.That(states[0]!["appearance"]!["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#232323"));
			Assert.That(states[0]!["appearance"]!["labelPosition"]!.GetValue<string>(), Is.EqualTo("top"));
			Assert.That(states[1]!["id"]!.GetValue<string>(), Is.EqualTo("on"));
			Assert.That(states[1]!["appearance"]!["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#ff0000"));
			Assert.That(states[1]!["appearance"]!["labelPosition"]!.GetValue<string>(), Is.EqualTo("center"));
		});
	}

	// Macro Deck 2's press lists do not mean the same things as Macro Deck 3's triggers, so this pins the
	// intended correspondence rather than a rename. Actions is deliberately carried to onShortPress even
	// though Macro Deck 2 fires it on mouse-down: it is the list every ordinary button uses, and a
	// migrated deck should read as "pressed". See MacroDeck2FlowBuilder for the cost that trades away.
	[Test]
	public async Task Read_MapsMacroDeck2sPressListsOntoTheTriggersAButtonReadsAs()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			actions: [MacroDeck2Fixture.Action(GoToParentType)],
			actionsRelease: [MacroDeck2Fixture.Action(GoToParentType)],
			actionsLongPress: [MacroDeck2Fixture.Action(GoToParentType)],
			actionsLongPressRelease: [MacroDeck2Fixture.Action(GoToParentType)]));

		var flows = Flows(await SingleWidgetData());
		var byTrigger = flows.ToDictionary(flow => flow!["triggerType"]!.GetValue<string>());

		Assert.Multiple(() =>
		{
			Assert.That(byTrigger.Keys, Is.EquivalentTo(new[] { "onShortPress", "onLongPress", "onTouchEnd" }));
			Assert.That(Disabled(byTrigger["onShortPress"]), Is.False);
			Assert.That(Disabled(byTrigger["onLongPress"]), Is.False);
		});
	}

	// Both release lists need the one release trigger Macro Deck 3 has, and two flows may never share a
	// trigger type - the second would never run. They share one flow, and only the long-press-release
	// blocks inside it are switched off.
	[Test]
	public async Task Read_PutsBothReleaseListsInOneFlow_WithOnlyTheLongPressOnesSwitchedOff()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			actionsRelease: [MacroDeck2Fixture.Action(GoToParentType)],
			actionsLongPressRelease: [MacroDeck2Fixture.Action(GoToParentType)]));

		var flows = Flows(await SingleWidgetData());
		var release = flows.Single(flow => flow!["triggerType"]!.GetValue<string>() == "onTouchEnd")!;
		var blocks = release["children"]!.AsArray();

		Assert.Multiple(() =>
		{
			Assert.That(flows, Has.Count.EqualTo(1), "no two flows may share a trigger type");
			Assert.That(blocks, Has.Count.EqualTo(2));
			Assert.That(blocks[0]!["disabled"], Is.Null, "the short-release actions still run");
			Assert.That(blocks[1]!["disabled"]!.GetValue<bool>(), Is.True);
		});
	}

	[Test]
	public async Task Read_WithLongPressReleaseActions_ExplainsWhyTheyAreSwitchedOff()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			actionsLongPressRelease: [MacroDeck2Fixture.Action(GoToParentType)]));

		var plan = await Read();

		Assert.That(plan.Warnings.Select(warning => warning.Kind),
			Does.Contain(MigrationWarningKind.UnsupportedTrigger));
	}

	[Test]
	public async Task Read_RewritesAFolderSwitchersTargetOntoTheMigratedFolder()
	{
		_fixture.WithProfile("0",
				"Profile",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[
							MacroDeck2Fixture.Button(0,
								0,
								actions: [MacroDeck2Fixture.Action(FolderSwitcherType, "sub")])
						],
						"sub"),
					MacroDeck2Fixture.Folder("sub", "Target", [])
				])
			.Build();

		var plan = await Read();
		var profile = plan.Profiles.Single().Profile!;
		var target = profile.Folders.Single(folder => folder.Name == "Target");
		var block = Flows(WidgetData(profile)).Single()!["children"]!.AsArray().Single()!;

		Assert.Multiple(() =>
		{
			Assert.That(block["integrationId"]!.GetValue<string>(), Is.EqualTo("app.macro-deck.deck"));
			Assert.That(block["actionId"]!.GetValue<string>(), Is.EqualTo("change-folder"));
			Assert.That(Parameter(block, "folderId"),
				Is.EqualTo(target.Id.ToString()),
				"the stored Macro Deck 2 folder id is rewritten to the folder that now exists");
		});
	}

	// An action nothing claims must still reach the deck carrying everything the source knew, and must
	// fail rather than do nothing quietly - hence enabled, not disabled.
	[Test]
	public async Task Read_WithAnActionNoMigratorClaims_KeepsItAsAnEnabledPlaceholder()
	{
		await SingleButton(MacroDeck2Fixture.Button(0,
			0,
			actions: [MacroDeck2Fixture.Action(UnknownActionType, """{"speed":3}""", "Do thing")]));

		var plan = await Read();
		var block = Flows(WidgetData(plan.Profiles.Single().Profile!)).Single()!["children"]!.AsArray().Single()!;

		Assert.Multiple(() =>
		{
			Assert.That(block["integrationId"]!.GetValue<string>(), Is.EqualTo(MigrationPlaceholder.IntegrationId));
			Assert.That(block["actionId"]!.GetValue<string>(), Is.EqualTo(MigrationPlaceholder.ActionId));
			Assert.That(block["disabled"], Is.Null, "a placeholder runs and fails rather than being skipped");
			Assert.That(Parameter(block, MigrationPlaceholder.SourceActionParameter),
				Is.EqualTo("Some.Vendor.Plugin.Actions.DoThingAction, Some Vendor Plugin"));
			Assert.That(Parameter(block, MigrationPlaceholder.SourceConfigurationParameter),
				Is.EqualTo("""{"speed":3}"""));
			Assert.That(plan.UnsupportedActions.Single().Occurrences, Is.EqualTo(1));
			Assert.That(plan.MigratedActionCount, Is.Zero);
		});
	}

	[Test]
	public async Task Read_WithAButtonOutsideTheGrid_DropsItAndSaysSo()
	{
		_fixture.WithProfile("0",
				"Profile",
				rows: 2,
				columns: 2,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[MacroDeck2Fixture.Button(0, 0), MacroDeck2Fixture.Button(7, 0)])
				])
			.Build();

		var plan = await Read();

		Assert.Multiple(() =>
		{
			Assert.That(plan.Profiles.Single().Profile!.Folders.Single().Widgets, Has.Count.EqualTo(1));
			Assert.That(plan.Warnings.Select(warning => warning.Kind),
				Does.Contain(MigrationWarningKind.SkippedWidget));
		});
	}

	[Test]
	public async Task Read_WithAnIconFromAPackThatIsNotInstalled_WarnsAndLeavesNoDanglingReference()
	{
		_fixture.WithIconPack("Powered Steel", "play")
			.WithProfile("0",
				"Profile",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[
							MacroDeck2Fixture.Button(0, 0, iconOff: "Powered Steel.play", iconOn: "Powered Steel.play"),
							MacroDeck2Fixture.Button(1,
								0,
								iconOff: "My Icon Pack.missing",
								iconOn: "My Icon Pack.missing")
						])
				])
			.Build();

		var plan = await Read();
		var widgets = plan.Profiles.Single().Profile!.Folders.Single().Widgets;
		var withMissingIcon = JsonNode.Parse(widgets[1].Data!)!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(plan.Icons, Has.Count.EqualTo(1), "only the resolvable icon is scheduled for import");
			Assert.That(plan.Icons[0].Name, Is.EqualTo("play"));
			Assert.That(withMissingIcon["icon"], Is.Null, "an unresolvable icon leaves no reference behind");
			Assert.That(plan.Warnings.Where(warning => warning.Kind == MigrationWarningKind.MissingIcon)
					.Select(warning => warning.Subject),
				Is.EquivalentTo(new[] { "My Icon Pack.missing" }));
		});
	}

	// A referenced pack comes over whole: taking only the icons a button happens to use would leave the
	// pack looking like a handful of loose images the next time that button is edited.
	[Test]
	public async Task Read_MigratesAReferencedIconPackWhole_NotJustTheIconsInUse()
	{
		_fixture.WithIconPack("Powered Steel", "play", "pause", "stop")
			.WithIconPack("Unused Pack", "nobody")
			.WithProfile("0",
				"Profile",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[MacroDeck2Fixture.Button(0, 0, iconOff: "Powered Steel.play", iconOn: "Powered Steel.play")])
				])
			.Build();

		var plan = await Read();

		Assert.Multiple(() =>
		{
			Assert.That(plan.Icons.Select(icon => icon.Name), Is.EquivalentTo(new[] { "play", "pause", "stop" }));
			Assert.That(plan.Icons.Select(icon => icon.PackName).Distinct(),
				Is.EquivalentTo(new[] { "Powered Steel" }),
				"a pack nothing references is not dragged along");
		});
	}

	[Test]
	public async Task Read_MigratesOnlyUserCreatedVariables()
	{
		_fixture.WithVariable("cs2_wins", "101", "User", "Integer")
			.WithVariable("spotify_title", "Song", "SpotifyPlugin", "String")
			.WithVariable("is_live", "False", "User", "Bool")
			.WithProfile("0", "Profile", 3, 5, [MacroDeck2Fixture.Folder("root", "*Root*", [])])
			.Build();

		var plan = await Read();

		Assert.That(plan.Variables.Select(variable => (variable.Name, variable.Type)),
			Is.EquivalentTo(new[] { ("cs2_wins", VariableType.Numeric), ("is_live", VariableType.Boolean) }));
	}

	// Macro Deck 3 constrains variable names, so a renamed variable has to take every reference to it
	// along - otherwise the label keeps rendering the old name literally and the state binding resolves
	// to nothing.
	[Test]
	public async Task Read_WhenAVariableNameHasToChange_RewritesTheReferencesToIt()
	{
		_fixture.WithVariable("Battery Level", "22", "User", "Integer")
			.WithProfile("0",
				"Profile",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[
							MacroDeck2Fixture.Button(0,
								0,
								labelOff: MacroDeck2Fixture.Label("{Battery Level}%"),
								labelOn: MacroDeck2Fixture.Label("{Battery Level}%"),
								stateBindingVariable: "Battery Level")
						])
				])
			.Build();

		var plan = await Read();
		var migratedName = plan.Variables.Single().Name;
		var data = WidgetData(plan.Profiles.Single().Profile!);
		var rule = data["stateMapping"]!["rules"]!.AsArray().Single()!;

		Assert.Multiple(() =>
		{
			Assert.That(migratedName, Is.EqualTo("battery_level"));
			Assert.That(data["states"]![0]!["appearance"]!["label"]!.GetValue<string>(),
				Is.EqualTo("{battery_level}%"));
			Assert.That(rule["when"]!["left"]!["$var"]!.GetValue<string>(), Is.EqualTo("battery_level"));
			Assert.That(rule["stateId"]!.GetValue<string>(), Is.EqualTo("on"));
		});
	}

	[Test]
	public async Task Read_WithoutAKey_ReportsThatItCouldNotReadOneRatherThanFailing()
	{
		SeedCredentials();

		var plan = await Read(machineKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(plan.CredentialStatus, Is.EqualTo(MigrationCredentialStatus.KeyUnavailable));
			Assert.That(plan.Profiles, Is.Not.Empty, "everything unencrypted still migrates");
		});
	}

	[Test]
	public async Task Read_WithTheKeyMacroDeck2Used_DecryptsTheStoredCredentials()
	{
		SeedCredentials();

		var plan = await Read(machineKey: MachineKey);

		Assert.That(plan.CredentialStatus, Is.EqualTo(MigrationCredentialStatus.Decrypted));
	}

	[Test]
	public async Task Read_WithASuppliedKeyThatDoesNotOpenTheCredentials_IsRejected()
	{
		SeedCredentials();

		var result = await ReadRaw(machineKey: null, decryptionKey: "11111111-1111-1111-1111-111111111111");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(MigrationError.InvalidDecryptionKey));
		});
	}

	// The warning is only worth raising for a plugin an integration here could actually have been
	// configured from - naming one nothing claims would be noise the user cannot act on.
	[Test]
	public async Task Read_WhenDecryptionIsSkipped_MigratesEverythingElseAndNamesWhatItLeftOut()
	{
		SeedCredentials();
		_migrators = new ClaimingRegistry("vendor_test plugin");

		var plan = await Read(machineKey: MachineKey, skipDecryption: true);

		Assert.Multiple(() =>
		{
			Assert.That(plan.CredentialStatus, Is.EqualTo(MigrationCredentialStatus.Skipped));
			Assert.That(plan.Profiles, Is.Not.Empty);
			Assert.That(plan.Warnings.Where(warning => warning.Kind == MigrationWarningKind.SkippedCredentials)
					.Select(warning => warning.Subject),
				Does.Contain("vendor_test plugin"));
		});
	}

	// A backup archive holds the same data directory at its root, so it must migrate to exactly what the
	// live folder does - the reader unpacks it and everything downstream sees one shape.
	[Test]
	public async Task Read_FromABackupArchive_ProducesTheSameResultAsTheFolderItWasPackedFrom()
	{
		_fixture.WithConfig()
			.WithVariable("cs2_wins", "101", "User", "Integer")
			.WithIconPack("Powered Steel", "play")
			.WithProfile("0",
				"Profil 1",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[MacroDeck2Fixture.Button(0, 0, iconOff: "Powered Steel.play", iconOn: "Powered Steel.play")],
						"sub"),
					MacroDeck2Fixture.Folder("sub", "SinusBot", [MacroDeck2Fixture.Button(1, 0)])
				])
			.Build();

		var fromFolder = await Read();
		var backup = _fixture.PackAsBackup();
		try
		{
			var result = await CreateSource(MachineKey).Read(
				new MigrationRequest(MacroDeck2MigrationSource.SourceId, backup, null, false),
				CancellationToken.None);

			Assert.That(result.Success, Is.True, result.ErrorMessage);
			var fromBackup = result.Data!;

			Assert.Multiple(() =>
			{
				Assert.That(fromBackup.Profiles, Has.Count.EqualTo(fromFolder.Profiles.Count));
				Assert.That(fromBackup.FolderCount, Is.EqualTo(fromFolder.FolderCount));
				Assert.That(fromBackup.WidgetCount, Is.EqualTo(fromFolder.WidgetCount));
				Assert.That(fromBackup.Icons, Has.Count.EqualTo(fromFolder.Icons.Count));
				Assert.That(fromBackup.Variables.Select(variable => variable.Name),
					Is.EquivalentTo(fromFolder.Variables.Select(variable => variable.Name)));
			});
		}
		finally
		{
			File.Delete(backup);
		}
	}

	[Test]
	public async Task Read_FromABackupArchiveThatHoldsNoProfiles_SaysSo()
	{
		var empty = Path.Combine(Path.GetTempPath(), $"md2-empty-{Guid.NewGuid():N}.zip");
		using (var stream = File.Create(empty))
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
		{
			zip.CreateEntry("readme.txt");
		}

		try
		{
			var result = await CreateSource(MachineKey).Read(
				new MigrationRequest(MacroDeck2MigrationSource.SourceId, empty, null, false),
				CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.False);
				Assert.That(result.Error, Is.EqualTo(MigrationError.SourceNotFound));
			});
		}
		finally
		{
			File.Delete(empty);
		}
	}

	[Test]
	public void Recognizes_AcceptsBothADataDirectoryAndABackupArchive()
	{
		_fixture.WithProfile("0", "Profile", 3, 5, [MacroDeck2Fixture.Folder("root", "*Root*", [])]).Build();
		var backup = _fixture.PackAsBackup();
		try
		{
			var source = CreateSource(MachineKey);

			Assert.Multiple(() =>
			{
				Assert.That(source.Recognizes(_fixture.Root), Is.True);
				Assert.That(source.Recognizes(backup), Is.True);
				Assert.That(source.Recognizes(Path.GetTempPath()), Is.False);
			});
		}
		finally
		{
			File.Delete(backup);
		}
	}

	[Test]
	public async Task Read_WithAFolderThatIsNotAMacroDeck2Directory_SaysSo()
	{
		var empty = Path.Combine(Path.GetTempPath(), "not-macro-deck-2", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(empty);
		try
		{
			var source = CreateSource(machineKey: null);
			var result = await source.Read(new MigrationRequest(MacroDeck2MigrationSource.SourceId, empty, null, false),
				CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(result.Success, Is.False);
				Assert.That(result.Error, Is.EqualTo(MigrationError.SourceNotFound));
			});
		}
		finally
		{
			Directory.Delete(empty, recursive: true);
		}
	}

	private void SeedCredentials()
	{
		_fixture.WithProfile("0", "Profile", 3, 5, [MacroDeck2Fixture.Folder("root", "*Root*", [])])
			.WithPluginCredentials("vendor_test plugin",
				MachineKey,
				new Dictionary<string, string> { ["token"] = "a-token" })
			.Build();
	}

	private async Task SingleButton(JsonObject button)
	{
		_fixture.WithProfile("0",
				"Profile",
				rows: 3,
				columns: 5,
				folders: [MacroDeck2Fixture.Folder("root", "*Root*", [button])])
			.Build();

		await Task.CompletedTask;
	}

	private async Task<JsonObject> SingleWidgetData() => WidgetData((await Read()).Profiles.Single().Profile!);

	private static JsonObject WidgetData(ProfileFile profile)
		=> JsonNode.Parse(profile.Folders.SelectMany(folder => folder.Widgets).First().Data!)!.AsObject();

	private static JsonArray Flows(JsonObject data) => JsonNode.Parse(data["flows"]!.GetValue<string>())!.AsArray();

	private static bool Disabled(JsonNode? flow)
		=> flow!["children"]!.AsArray().All(block => block!["disabled"]?.GetValue<bool>() == true);

	private static string? Parameter(JsonNode block, string name)
		=> block["parameters"]!.AsArray()
			.FirstOrDefault(parameter => parameter!["name"]!.GetValue<string>() == name)
			?["value"]
			?.GetValue<string>();

	private async Task<MigrationPlan> Read(string? machineKey = MachineKey, bool skipDecryption = false)
	{
		var result = await ReadRaw(machineKey, null, skipDecryption);
		Assert.That(result.Success, Is.True, result.ErrorMessage);
		return result.Data!;
	}

	private async Task<Domain.Common.Result<MigrationPlan, MigrationError>> ReadRaw(
		string? machineKey,
		string? decryptionKey = null,
		bool skipDecryption = false)
		=> await CreateSource(machineKey).Read(
			new MigrationRequest(MacroDeck2MigrationSource.SourceId, _fixture.Root, decryptionKey, skipDecryption),
			CancellationToken.None);

	private MacroDeck2MigrationSource CreateSource(string? machineKey)
		=> new(_migrators, new StubMachineKeyReader(machineKey), new LoggerConfiguration().CreateLogger());

	private sealed class StubMachineKeyReader : IMacroDeck2MachineKeyReader
	{
		private readonly string? _key;

		public StubMachineKeyReader(string? key) => _key = key;

		public string? TryRead() => _key;
	}

	/// <summary>No integration claims anything, so only the built-in translations apply.</summary>
	private sealed class NoMigratorRegistry : IMigrationActionRegistry
	{
		public IIntegrationMigration? FindByActionSource(MigrationSource source, string actionSource) => null;

		public IIntegrationMigration? FindBySettingsSource(MigrationSource source, string settingsSource) => null;

		public IReadOnlyList<MigrationSource> SupportedSources() => [];
	}

	private sealed class ClaimingRegistry : IMigrationActionRegistry
	{
		private readonly string _settingsSource;

		public ClaimingRegistry(string settingsSource) => _settingsSource = settingsSource;

		public IIntegrationMigration? FindByActionSource(MigrationSource source, string actionSource) => null;

		public IIntegrationMigration? FindBySettingsSource(MigrationSource source, string settingsSource)
			=> string.Equals(settingsSource, _settingsSource, StringComparison.OrdinalIgnoreCase)
				? new Migration()
				: null;

		public IReadOnlyList<MigrationSource> SupportedSources() => [MigrationSource.MacroDeck2];

		private sealed class Migration : IIntegrationMigration
		{
			public MigrationSource Source => MigrationSource.MacroDeck2;

			public IReadOnlyList<string> ClaimedActionSources => [];

			public IReadOnlyList<string> ClaimedSettingsSources => [];

			public Task<ActionMigrationResult?> MigrateActionAsync(
				ForeignAction action,
				CancellationToken cancellationToken)
				=> Task.FromResult<ActionMigrationResult?>(null);

			public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
				ForeignPluginSettings settings,
				CancellationToken cancellationToken)
				=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);
		}
	}
}
