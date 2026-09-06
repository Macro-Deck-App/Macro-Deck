using System.Text.Json;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
internal sealed class RemotePluginSnapshotStoreTests
{
	/// <summary>
	/// A document exactly as the store wrote it before ADR 0081 merged <c>dynamic-variables</c> into
	/// <c>variables</c>: no <c>version</c>, a <c>declaredVariables</c> section carrying
	/// <c>providedVariables</c> and descriptors keyed by <c>definitionId</c>, and an action descriptor
	/// still carrying <c>sliderValueParameter</c>. Written out as a literal rather than produced by the
	/// current code, because the point of the test is that today's reader still understands yesterday's
	/// bytes.
	/// </summary>
	private const string PreAdr0081Document = """
											  {
											    "com.example.plugin": {
											      "actions": {
											        "actions": [
											          {
											            "localId": "set-volume",
											            "name": "Set volume",
											            "description": "Sets the output volume",
											            "parameters": [],
											            "sliderValueParameter": "volume",
											            "supportsDynamicOptions": false,
											            "providesState": true,
											            "configuresWithUiTree": false,
											            "providesIcon": true
											          }
											        ]
											      },
											      "allowsMultipleConfigurations": true,
											      "acceptedKinds": [ "actions", "variables", "dynamic-variables", "events", "icons", "weather" ],
											      "declaredVariables": {
											        "declaredVariables": [
											          {
											            "name": "example_cpu_usage",
											            "type": "Numeric",
											            "decimalPlaces": 1,
											            "refreshIntervalSeconds": 5,
											            "definitionId": "cpu-usage",
											            "displayName": "CPU usage"
											          }
											        ],
											        "providedVariables": [
											          {
											            "name": "example_cpu_usage",
											            "type": "Numeric",
											            "decimalPlaces": 1,
											            "refreshIntervalSeconds": 5,
											            "definitionId": "cpu-usage",
											            "displayName": "CPU usage"
											          },
											          {
											            "name": "example_status",
											            "type": "Text",
											            "configurationKey": "account",
											            "configurationName": "Account"
											          }
											        ],
											        "variablesDependOnConfiguration": true
											      },
											      "events": {
											        "providerName": "Example",
											        "events": [
											          {
											            "localId": "scene-changed",
											            "name": "Scene changed",
											            "deliveryKind": "Push",
											            "configurationParameters": [],
											            "payloadParameters": []
											          }
											        ],
											        "hasDynamicEventOptions": true
											      },
											      "icon": {
											        "iconBytes": "AQID",
											        "iconMimeType": "image/png",
											        "hasIcon": true,
											        "iconContentHash": "hash-a",
											        "iconBytesContentHash": "hash-b"
											      },
											      "weather": {
											        "providerName": "Example Weather",
											        "instances": [ { "id": "home", "displayName": "Home" } ]
											      },
											      "virtualProfiles": {
											        "providerName": "Example Profiles",
											        "profiles": []
											      }
											    }
											  }
											  """;

	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task Accepted_kinds_round_trip_through_a_fresh_store()
	{
		var store = new RemotePluginSnapshotStore(_paths, Log.Logger);

		await store.SaveAsync(RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Icons, CapabilityKinds.ConfigFlow]
		});

		var reloaded = new RemotePluginSnapshotStore(_paths, Log.Logger).GetSnapshot("com.example.plugin");

		Assert.That(reloaded.AcceptedKinds,
			Is.EquivalentTo(new[] { CapabilityKinds.Icons, CapabilityKinds.ConfigFlow }));
	}

	[Test]
	public void A_persisted_file_with_no_accepted_kinds_field_loads_without_throwing()
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		var path = Path.Combine(_paths.ConfigDirectory, "plugin-capability-snapshots.json");

		File.WriteAllText(path,
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["com.example.plugin"] = new
				{
					Actions = new { Actions = Array.Empty<object>() }, AllowsMultipleConfigurations = true
				}
			}));

		RemotePluginSnapshotStore? store = null;
		Assert.DoesNotThrow(() => store = new RemotePluginSnapshotStore(_paths, Log.Logger));

		var snapshot = store!.GetSnapshot("com.example.plugin");
		Assert.That(snapshot.AcceptedKinds, Is.Empty);
	}

	[Test]
	public async Task Variables_and_events_sections_round_trip_through_a_fresh_store()
	{
		var store = new RemotePluginSnapshotStore(_paths, Log.Logger);

		await store.SaveAsync(RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			DeclaredVariables = [VariableDefinition.Eager("cpu_temp", VariableType.Numeric)],
			Variables =
			[
				VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with
				{
					Id = "cpu-temp",
					Unit = "°C",
					SemanticKind = VariableSemanticKinds.None,
					Write = new VariableWriteCapability { CommitOnRelease = true }
				}
			],
			VariablesDependOnConfiguration = true,
			SupportsVariableCatalog = true,
			SupportsVariablePush = true,
			SupportsVariableSearch = true,
			VariableCatalogName = "Example entities",
			EventProviderName = "OBS Studio",
			EventDefinitions = [new EventDefinition { Id = "scene-changed", Name = "Scene changed" }],
			HasDynamicEventOptions = true
		});

		var reloaded = new RemotePluginSnapshotStore(_paths, Log.Logger).GetSnapshot("com.example.plugin");
		var declaredVariableNames = reloaded.DeclaredVariables.Select(v => v.Name).ToList();
		var eventIds = reloaded.EventDefinitions.Select(e => e.Id).ToList();
		var variable = reloaded.Variables.Single();
		string[] expectedVariableNames = ["cpu_temp"];
		string[] expectedEventIds = ["scene-changed"];

		Assert.Multiple(() =>
		{
			Assert.That(declaredVariableNames, Is.EqualTo(expectedVariableNames));
			Assert.That(variable.ResolvedId, Is.EqualTo("cpu-temp"));
			Assert.That(variable.Materialization, Is.EqualTo(VariableMaterialization.Eager));
			Assert.That(variable.Unit, Is.EqualTo("°C"));
			Assert.That(variable.Write?.CommitOnRelease, Is.True);
			Assert.That(reloaded.VariablesDependOnConfiguration, Is.True);
			Assert.That(reloaded.SupportsVariableCatalog, Is.True);
			Assert.That(reloaded.SupportsVariablePush, Is.True);
			Assert.That(reloaded.SupportsVariableSearch, Is.True);
			Assert.That(reloaded.VariableCatalogName, Is.EqualTo("Example entities"));
			Assert.That(reloaded.EventProviderName, Is.EqualTo("OBS Studio"));
			Assert.That(eventIds, Is.EqualTo(expectedEventIds));
			Assert.That(reloaded.HasDynamicEventOptions, Is.True);
		});
	}

	[Test]
	public void A_persisted_file_with_no_variables_or_events_fields_loads_without_throwing()
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		var path = Path.Combine(_paths.ConfigDirectory, "plugin-capability-snapshots.json");

		File.WriteAllText(path,
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["com.example.plugin"] = new
				{
					Actions = new { Actions = Array.Empty<object>() },
					AllowsMultipleConfigurations = true,
					AcceptedKinds = new[] { CapabilityKinds.Actions }
				}
			}));

		RemotePluginSnapshotStore? store = null;
		Assert.DoesNotThrow(() => store = new RemotePluginSnapshotStore(_paths, Log.Logger));

		var snapshot = store!.GetSnapshot("com.example.plugin");
		Assert.Multiple(() =>
		{
			Assert.That(snapshot.DeclaredVariables, Is.Empty);
			Assert.That(snapshot.Variables, Is.Empty);
			Assert.That(snapshot.VariablesDependOnConfiguration, Is.False);
			Assert.That(snapshot.EventProviderName, Is.Empty);
			Assert.That(snapshot.EventDefinitions, Is.Empty);
			Assert.That(snapshot.HasDynamicEventOptions, Is.False);
		});
	}

	[Test]
	public void A_pre_adr_0081_document_keeps_every_cached_capability()
	{
		WritePreAdr0081Document();

		var snapshot = new RemotePluginSnapshotStore(_paths, Log.Logger).GetSnapshot("com.example.plugin");
		var eagerNames = snapshot.Variables.Select(variable => variable.Name).ToList();
		string[] expectedEagerNames = ["example_cpu_usage", "example_status"];

		Assert.Multiple(() =>
		{
			// The variable half, in its new shape.
			Assert.That(eagerNames, Is.EqualTo(expectedEagerNames));
			Assert.That(snapshot.Variables[0].ResolvedId, Is.EqualTo("cpu-usage"));
			Assert.That(snapshot.Variables[0].Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(snapshot.Variables[0].DecimalPlaces, Is.EqualTo(1));
			Assert.That(snapshot.Variables[0].RefreshInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
			Assert.That(snapshot.Variables.All(v => v.Materialization == VariableMaterialization.Eager), Is.True);
			Assert.That(snapshot.Variables[1].Configuration?.Key, Is.EqualTo("account"));
			Assert.That(snapshot.DeclaredVariables.Single().Name, Is.EqualTo("example_cpu_usage"));
			Assert.That(snapshot.VariablesDependOnConfiguration, Is.True);

			// The retired kind is still what tells the host this plugin had a catalog.
			Assert.That(snapshot.SupportsVariableCatalog, Is.True);

			// Everything else in the document survives - a variables-only change must not quarantine it.
			Assert.That(snapshot.Actions.Single().LocalId, Is.EqualTo("set-volume"));
			Assert.That(snapshot.Actions.Single().ProvidesState, Is.True);
			Assert.That(snapshot.EventProviderName, Is.EqualTo("Example"));
			Assert.That(snapshot.EventDefinitions.Single().Id, Is.EqualTo("scene-changed"));
			Assert.That(snapshot.HasIcon, Is.True);
			Assert.That(snapshot.IconBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
			Assert.That(snapshot.WeatherProviderName, Is.EqualTo("Example Weather"));
			Assert.That(snapshot.WeatherInstances.Single().Id, Is.EqualTo("home"));
			Assert.That(snapshot.ProfileProviderName, Is.EqualTo("Example Profiles"));
		});
	}

	[Test]
	public void A_pre_adr_0081_document_without_the_retired_kind_reports_no_catalog()
	{
		WritePreAdr0081Document(PreAdr0081Document.Replace("\"dynamic-variables\", ",
			string.Empty,
			StringComparison.Ordinal));

		var snapshot = new RemotePluginSnapshotStore(_paths, Log.Logger).GetSnapshot("com.example.plugin");

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.SupportsVariableCatalog, Is.False);
			Assert.That(snapshot.Variables, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_pre_adr_0081_document_is_rewritten_in_the_current_shape_without_losing_a_variable()
	{
		WritePreAdr0081Document();

		var store = new RemotePluginSnapshotStore(_paths, Log.Logger);
		await store.SaveAsync(store.GetSnapshot("com.example.plugin"));

		var reloaded = new RemotePluginSnapshotStore(_paths, Log.Logger).GetSnapshot("com.example.plugin");
		var eagerNames = reloaded.Variables.Select(variable => variable.Name).ToList();
		string[] expectedEagerNames = ["example_cpu_usage", "example_status"];

		Assert.Multiple(() =>
		{
			Assert.That(eagerNames, Is.EqualTo(expectedEagerNames));
			Assert.That(reloaded.Variables[0].ResolvedId, Is.EqualTo("cpu-usage"));
			Assert.That(reloaded.SupportsVariableCatalog, Is.True);
			Assert.That(reloaded.Actions.Single().LocalId, Is.EqualTo("set-volume"));
		});
	}

	private void WritePreAdr0081Document(string? document = null)
	{
		Directory.CreateDirectory(_paths.ConfigDirectory);
		File.WriteAllText(Path.Combine(_paths.ConfigDirectory, "plugin-capability-snapshots.json"),
			document ?? PreAdr0081Document);
	}
}
