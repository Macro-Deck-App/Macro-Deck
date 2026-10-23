using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Tests.UnitTests.MusicPlayer;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class IntegrationUiProviderResolutionTests : UiSessionFixture
{
	private const string MusicPlayerIntegrationId = "app.macro-deck.music-player";

	[SetUp]
	public void AddMusicPlayerProviders()
	{
		var players = new SinglePlayerRegistry(new FakeDevicePlayer());

		BuiltInIntegrationProviders.Add(new MusicPlayerPickerUiProvider(players,
			new NoArtwork(),
			new UiResourceStore(),
			Serilog.Core.Logger.None));
		BuiltInIntegrationProviders.Add(new MusicPlayerDevicePickerUiProvider(players, Serilog.Core.Logger.None));
	}

	[Test]
	public async Task The_device_picker_dialog_opens_although_another_provider_of_the_integration_comes_first()
	{
		var settled = await OpenDialogAsync(MusicPlayerDevicePickerUiProvider.PickViewId);

		Assert.That(settled.Accepted, Is.True, $"{settled.Code}: {settled.Message}");
	}

	[Test]
	public async Task The_track_picker_dialog_still_opens()
	{
		var settled = await OpenDialogAsync(MusicPlayerPickerUiProvider.PickViewId);

		Assert.That(settled.Accepted, Is.True, $"{settled.Code}: {settled.Message}");
	}

	[Test]
	public async Task A_dialog_no_provider_of_the_integration_serves_is_rejected()
	{
		var settled = await OpenDialogAsync("music-player-unknown");

		Assert.Multiple(() =>
		{
			Assert.That(settled.Accepted, Is.False);
			Assert.That(settled.Code, Is.EqualTo(UiSessionErrorCodes.ProviderRejected));
		});
	}

	private async Task<UiSessionOpenTicket> OpenDialogAsync(string viewId)
	{
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(viewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(
					new Dictionary<string, string>(StringComparer.Ordinal) { ["instanceId"] = "inst-1" })
			}
		};

		var ticket = await Broker.OpenAsync(MusicPlayerIntegrationId, surface, DeviceA, CancellationToken.None);

		return await ticket.Ready.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private sealed class NoArtwork : IMusicPlayerArtworkService
	{
		public string GetETag(string artworkId, int? size)
		{
			return artworkId;
		}

		public Task<ArtworkImageResult?> GetImage(
			string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<ArtworkImageResult?>(null);
		}

		public Task<ArtworkImageResult?> GetImage(
			MusicPlayerVariant variant,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<ArtworkImageResult?>(null);
		}
	}
}
