using System.Globalization;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

internal static class StreamlabsJson
{
	public const string SceneId = "scene_gameplay";
	public const string SceneName = "Gameplay";
	public const string OtherSceneId = "scene_brb";
	public const string OtherSceneName = "BRB";

	public const string MicSourceId = "wasapi_input_capture_mic";
	public const string MicSourceName = "Mic/Aux";
	public const string CameraSourceId = "dshow_input_cam";
	public const string CameraSourceName = "Webcam";

	public const string CameraItemId = "item_cam";

	public static string Scenes { get; } = $$"""
											 [
											 	{"_type":"HELPER","resourceId":"Scene[\"{{SceneId}}\"]","id":"{{SceneId}}","name":"{{SceneName}}"},
											 	{"_type":"HELPER","resourceId":"Scene[\"{{OtherSceneId}}\"]","id":"{{OtherSceneId}}","name":"{{OtherSceneName}}"}
											 ]
											 """;

	public static string ActiveScene { get; } =
		$$"""{"_type":"HELPER","resourceId":"Scene[\"{{SceneId}}\"]","id":"{{SceneId}}","name":"{{SceneName}}"}""";

	public static string Sources { get; } = $$"""
											  [
											  	{"sourceId":"{{MicSourceId}}","name":"{{MicSourceName}}","type":"wasapi_input_capture"},
											  	{"sourceId":"{{CameraSourceId}}","name":"{{CameraSourceName}}","type":"dshow_input"}
											  ]
											  """;

	public static string AudioSources { get; } = $$"""
												   [
												   	{
												   		"_type":"HELPER",
												   		"resourceId":"AudioSource[\"{{MicSourceId}}\"]",
												   		"sourceId":"{{MicSourceId}}",
												   		"name":"{{MicSourceName}}",
												   		"muted":false,
												   		"fader":{"db":0,"deflection":0.8,"mul":1}
												   	}
												   ]
												   """;

	public static string MicResource { get; } = StreamlabsRpcFrames.AudioSourceResource(MicSourceId);

	public static string SceneResource { get; } = StreamlabsRpcFrames.SceneResource(SceneId);

	public static string SceneItemResource { get; } =
		StreamlabsRpcFrames.SceneItemResource(SceneId, CameraItemId, CameraSourceId);

	public static string CameraItemResource { get; } =
		SceneItemResource.Replace("\"", "\\\"", StringComparison.Ordinal);

	public static string SceneItems { get; } = $$"""
												 [
												 	{
												 		"_type":"HELPER",
												 		"resourceId":"{{CameraItemResource}}",
												 		"sceneId":"{{SceneId}}",
												 		"sceneItemId":"{{CameraItemId}}",
												 		"sourceId":"{{CameraSourceId}}",
												 		"visible":true
												 	}
												 ]
												 """;

	public static string StreamingModel(
		string streaming = "offline",
		string recording = "offline",
		string replayBuffer = "offline") => $$"""
											  {
											  	"streamingStatus":"{{streaming}}",
											  	"streamingStatusTime":"2026-01-01T10:00:00.000Z",
											  	"recordingStatus":"{{recording}}",
											  	"recordingStatusTime":"2026-01-01T10:00:00.000Z",
											  	"replayBufferStatus":"{{replayBuffer}}",
											  	"replayBufferStatusTime":"2026-01-01T10:00:00.000Z"
											  }
											  """;

	public static string StudioMode(bool enabled) => $$"""{"studioMode":{{(enabled ? "true" : "false")}}}""";

	public static string AudioSource(bool muted = false, double deflection = 0.8) => $$"""
		  {
		  	"_type":"HELPER",
		  	"resourceId":"AudioSource[\"{{MicSourceId}}\"]",
		  	"sourceId":"{{MicSourceId}}",
		  	"name":"{{MicSourceName}}",
		  	"muted":{{(muted ? "true" : "false")}},
		  	"fader":{"db":0,"deflection":{{deflection.ToString(CultureInfo.InvariantCulture)}},"mul":1}
		  }
		  """;

	public static FakeStreamlabsClient Seeded(
		string streaming = "offline",
		string recording = "offline",
		string replayBuffer = "offline",
		bool studioMode = false)
	{
		var client = new FakeStreamlabsClient();
		client.Responses[$"{StreamlabsServices.Scenes}.{StreamlabsServices.GetScenes}"] = Scenes;
		client.Responses[$"{StreamlabsServices.Scenes}.{StreamlabsServices.ActiveScene}"] = ActiveScene;
		client.Responses[$"{StreamlabsServices.Sources}.{StreamlabsServices.GetSources}"] = Sources;
		client.Responses[$"{StreamlabsServices.Audio}.{StreamlabsServices.GetSources}"] = AudioSources;
		client.Responses[$"{StreamlabsServices.Streaming}.{StreamlabsServices.GetModel}"] =
			StreamingModel(streaming, recording, replayBuffer);
		client.Responses[$"{StreamlabsServices.Transitions}.{StreamlabsServices.GetModel}"] = StudioMode(studioMode);
		client.Responses[$"{SceneResource}.{StreamlabsServices.GetItems}"] = SceneItems;
		client.Responses[$"{MicResource}.{StreamlabsServices.GetModel}"] = AudioSource();
		return client;
	}
}
