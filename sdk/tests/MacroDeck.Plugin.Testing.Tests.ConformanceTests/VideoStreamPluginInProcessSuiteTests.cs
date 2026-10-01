using MacroDeck.Localization;
using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

[TestFixture]
internal sealed class VideoStreamPluginInProcessSuiteTests : ConformanceFixture
{
	protected override Task<ConformanceSubject> CreateSubjectAsync()
		=> Task.FromResult(ConformanceSubject.InProcess(builder => builder.RegisterIntegration(_ => new CameraOnlyIntegration()),
			new PluginTestManifest(id: "app.macro-deck.video-test-plugin", name: "Video Test Plugin", version: "1.0.0")));

	[TestCase("MDC0302")]
	[TestCase("MDC0303")]
	[TestCase("MDC0501")]
	[TestCase("MDC0503")]
	[TestCase("MDC0505")]
	public void A_video_only_plugin_is_exercised_by_the_capability_checks(string checkId)
		=> Assert.That(Report.Results.Single(result => result.Id == checkId).Result.Outcome,
			Is.EqualTo(ConformanceOutcome.Passed));

	private sealed class CameraOnlyIntegration : IPluginIntegration, IVideoStreamIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default)
			=> context.RegisterProviderAsync(new Camera(), cancellationToken);
	}

	private sealed class Camera : IVideoStreamProvider
	{
		public string Id => "front-door";

		public LocalizedText Name => LocalizedText.FromLiteral("Front door");

		public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<VideoStreamDescriptor>>(
				[new VideoStreamDescriptor("main", LocalizedText.FromLiteral("Main"))]);

		public Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));

		public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
			=> Task.CompletedTask;
	}
}
