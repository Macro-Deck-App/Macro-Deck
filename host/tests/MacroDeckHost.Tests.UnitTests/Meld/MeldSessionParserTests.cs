using System.Text.Json;
using MacroDeckHost.Integrations.Meld;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldSessionParserTests
{
	private const string ExampleSessionJson =
		"""
		{"items":{
		"05731BDA0C5DE934359C8F7F24CE6C1B":{"current":true,"index":1,"name":"Just Chatting","staged":false,"type":"scene"},
		"375F1BB5A322BCC7F60CAAD16EE859D7":{"monitoring":false,"muted":false,"name":"Spoilers","parent":"89BFE4955144FBD6490ECBB453CA6639","type":"track"},
		"6A92BA3C4FCC18180F76D3EFE9167EF0":{"height":699,"index":1,"name":"Fortnite","parent":"9DA5A633182EB0C0804E671B26A1B581","rotation":0,"type":"layer","visible":true,"width":1303,"x":265,"y":132},
		"7958A12F7C782F392B08A793218AB7E8":{"height":1080,"index":2,"isPlaying":true,"mediaSource":"file:///path/to/video.mp4","name":"Background","parent":"05731BDA0C5DE934359C8F7F24CE6C1B","rotation":0,"type":"layer","visible":true,"width":1920,"x":0,"y":0},
		"85DFC4BCD7451D2DB3C55A6E2D9948E2":{"enabled":true,"name":"Glow Styles","parent":"9F2A72924699F775E0E789C6940141F6","type":"effect"},
		"86DF5581538B7A73504368E4F02E242A":{"monitoring":false,"muted":false,"name":"Background","parent":"7958A12F7C782F392B08A793218AB7E8","type":"track"},
		"89BFE4955144FBD6490ECBB453CA6639":{"height":720,"index":1,"name":"Spoilers","parent":"05731BDA0C5DE934359C8F7F24CE6C1B","rotation":0,"type":"layer","url":"https://www.meldstudio.co/browser","visible":true,"width":1280,"x":118,"y":120},
		"8DB9CC06AFCA152EA30278380B569751":{"height":281,"index":0,"name":"Camera Frame","parent":"05731BDA0C5DE934359C8F7F24CE6C1B","rotation":0,"source":"file:///path/to/image.png","type":"layer","visible":true,"width":500,"x":1354,"y":156},
		"9DA5A633182EB0C0804E671B26A1B581":{"current":false,"index":0,"name":"Gaming","staged":false,"type":"scene"},
		"9F2A72924699F775E0E789C6940141F6":{"height":297,"index":0,"name":"Camera","parent":"9DA5A633182EB0C0804E671B26A1B581","rotation":0,"type":"layer","visible":true,"width":264,"x":1436,"y":683},
		"D4D3457F8AF5D1F5381A903A8C43BE27":{"monitoring":false,"muted":true,"name":"Microphone","type":"track"},
		"EAAF1F3600F53FE362F9CD8194CB1639":{"monitoring":false,"muted":false,"name":"Fortnite","parent":"6A92BA3C4FCC18180F76D3EFE9167EF0","type":"track"}
		}}
		""";

	private const string JustChattingSceneId = "05731BDA0C5DE934359C8F7F24CE6C1B";
	private const string GamingSceneId = "9DA5A633182EB0C0804E671B26A1B581";
	private const string GlowStylesEffectId = "85DFC4BCD7451D2DB3C55A6E2D9948E2";
	private const string SpoilersTrackId = "375F1BB5A322BCC7F60CAAD16EE859D7";
	private const string MicrophoneTrackId = "D4D3457F8AF5D1F5381A903A8C43BE27";

	[Test]
	public void The_example_session_parses_into_the_expected_counts()
	{
		var session = Parse(ExampleSessionJson);

		Assert.Multiple(() =>
		{
			Assert.That(session.Scenes, Has.Count.EqualTo(2));
			Assert.That(session.LayersById, Has.Count.EqualTo(5));
			Assert.That(session.EffectsById, Has.Count.EqualTo(1));
			Assert.That(session.Tracks, Has.Count.EqualTo(4));
		});
	}

	[Test]
	public void An_effects_scene_id_resolves_through_its_layer()
	{
		var session = Parse(ExampleSessionJson);

		var effect = session.EffectsById[GlowStylesEffectId];
		Assert.That(effect.SceneId, Is.EqualTo(GamingSceneId));
	}

	[Test]
	public void A_track_without_a_parent_is_global_and_one_with_a_layer_parent_carries_its_scene_name()
	{
		var session = Parse(ExampleSessionJson);

		var global = session.TracksById[MicrophoneTrackId];
		var scoped = session.TracksById[SpoilersTrackId];

		Assert.Multiple(() =>
		{
			Assert.That(global.ParentLayerId, Is.Null);
			Assert.That(global.SceneName, Is.Null);
			Assert.That(scoped.ParentLayerId, Is.Not.Null);
			Assert.That(scoped.SceneName, Is.EqualTo("Just Chatting"));
		});
	}

	[Test]
	public void Current_and_staged_scene_ids_are_resolved_and_null_when_none_are_staged()
	{
		var session = Parse(ExampleSessionJson);

		Assert.Multiple(() =>
		{
			Assert.That(session.CurrentSceneId, Is.EqualTo(JustChattingSceneId));
			Assert.That(session.StagedSceneId, Is.Null);
		});
	}

	private static readonly string[] _expectedSceneOrder = ["Gaming", "Just Chatting"];
	private static readonly string[] _expectedLayerOrder = ["Camera Frame", "Spoilers", "Background"];

	[Test]
	public void Scenes_and_layers_are_ordered_by_index()
	{
		var session = Parse(ExampleSessionJson);

		Assert.That(session.Scenes.Select(scene => scene.Name), Is.EqualTo(_expectedSceneOrder));

		var justChatting = session.Scenes.Single(scene => scene.Id == JustChattingSceneId);
		Assert.That(justChatting.Layers.Select(layer => layer.Name), Is.EqualTo(_expectedLayerOrder));
	}

	[Test]
	public void An_orphan_layer_is_dropped()
	{
		const string json =
			"""
			{"items":{
			"layer1":{"parent":"missing-scene","index":0,"name":"Orphan","visible":true,"type":"layer"}
			}}
			""";

		var session = Parse(json);

		Assert.That(session.LayersById, Is.Empty);
	}

	[Test]
	public void An_orphan_effect_is_dropped()
	{
		const string json =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},
			"effect1":{"parent":"missing-layer","name":"Orphan Effect","enabled":true,"type":"effect"}
			}}
			""";

		var session = Parse(json);

		Assert.Multiple(() =>
		{
			Assert.That(session.LayersById, Has.Count.EqualTo(1));
			Assert.That(session.EffectsById, Is.Empty);
		});
	}

	[Test]
	public void Malformed_items_are_skipped_without_throwing()
	{
		const string json =
			"""
			{"items":{
			"unknownType":{"name":"X","type":"gizmo"},
			"missingType":{"name":"X"},
			"notAnObject":"just a string",
			"badVisible":{"parent":"scene1","index":0,"name":"Bad","visible":"yes","type":"layer"},
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"}
			}}
			""";

		MeldSession session = default!;
		Assert.DoesNotThrow(() => session = Parse(json));

		Assert.Multiple(() =>
		{
			Assert.That(session.Scenes, Has.Count.EqualTo(1));
			Assert.That(session.LayersById, Is.Empty);
		});
	}

	[Test]
	public void StructuralHash_is_equal_for_two_identical_sessions()
	{
		var a = Parse(ExampleSessionJson);
		var b = Parse(ExampleSessionJson);

		Assert.That(a.StructuralHash, Is.EqualTo(b.StructuralHash));
	}

	[Test]
	public void StructuralHash_differs_on_a_rename_add_or_remove()
	{
		var baseline = Parse(ExampleSessionJson);

		var renamed = Parse(
			ExampleSessionJson.Replace("\"Just Chatting\"", "\"Just Vibing\"", StringComparison.Ordinal));
		Assert.That(renamed.StructuralHash, Is.Not.EqualTo(baseline.StructuralHash));

		const string added =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"scene2":{"current":false,"index":1,"name":"Extra","staged":false,"type":"scene"}
			}}
			""";
		const string withoutExtra =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"}
			}}
			""";

		Assert.That(Parse(added).StructuralHash, Is.Not.EqualTo(Parse(withoutExtra).StructuralHash));
	}

	[Test]
	public void StructuralHash_differs_when_a_layer_is_re_parented_to_a_different_scene()
	{
		const string inSceneOne =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene One","staged":false,"type":"scene"},
			"scene2":{"current":false,"index":1,"name":"Scene Two","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"}
			}}
			""";
		const string inSceneTwo =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene One","staged":false,"type":"scene"},
			"scene2":{"current":false,"index":1,"name":"Scene Two","staged":false,"type":"scene"},
			"layer1":{"parent":"scene2","index":0,"name":"Layer","visible":true,"type":"layer"}
			}}
			""";

		Assert.That(Parse(inSceneTwo).StructuralHash, Is.Not.EqualTo(Parse(inSceneOne).StructuralHash));
	}

	[Test]
	public void StructuralHash_is_unaffected_by_visible_or_muted_flipping()
	{
		const string visibleTrue =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":true,"type":"layer"},
			"track1":{"name":"Track","muted":false,"monitoring":false,"type":"track"}
			}}
			""";
		const string visibleFalse =
			"""
			{"items":{
			"scene1":{"current":false,"index":0,"name":"Scene","staged":false,"type":"scene"},
			"layer1":{"parent":"scene1","index":0,"name":"Layer","visible":false,"type":"layer"},
			"track1":{"name":"Track","muted":true,"monitoring":true,"type":"track"}
			}}
			""";

		Assert.That(Parse(visibleTrue).StructuralHash, Is.EqualTo(Parse(visibleFalse).StructuralHash));
	}

	private static MeldSession Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return MeldSessionParser.Parse(document.RootElement);
	}
}
