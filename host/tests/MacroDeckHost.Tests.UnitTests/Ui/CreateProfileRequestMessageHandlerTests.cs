using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Tests.UnitTests.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class CreateProfileRequestMessageHandlerTests
{
	[Test]
	public async Task A_profile_requested_during_startup_is_only_created_once_the_caches_are_ready()
	{
		var readiness = new StartupReadiness();
		var profiles = new CachingInitializeBackgroundServiceTests.RecordingProfileService();
		var handler = new CreateProfileRequestMessageHandler(profiles, TestColors.None, readiness);

		var pending = handler.Handle(new CreateProfileRequest { Name = "Early" }, CancellationToken.None);
		Assert.That(profiles.CreatedNames, Is.Empty);

		readiness.MarkCachesReady();
		var response = await pending;

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(profiles.CreatedNames, Is.EqualTo(new[] { "Early" }));
		});
	}
}
