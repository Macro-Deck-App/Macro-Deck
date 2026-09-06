using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.System;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class GetAboutInfoRequestMessageHandlerTests
{
	[Test]
	public async Task Handle_returns_version_license_and_environment_details()
	{
		var handler = new GetAboutInfoRequestMessageHandler();

		var response = await handler.Handle(new GetAboutInfoRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Version, Is.EqualTo(HostVersion.Current));
			Assert.That(response.IsBeta, Is.EqualTo(HostVersion.IsBeta));
			Assert.That(response.IsDevelopmentBuild, Is.EqualTo(BuildConfig.Channel == BuildChannel.Development));
			Assert.That(response.License, Is.EqualTo("Apache-2.0"));
			Assert.That(response.RuntimeVersion, Does.Contain(".NET"));
			Assert.That(response.OperatingSystem, Is.Not.Empty);
			Assert.That(response.Commit, Is.EqualTo(HostBuildInfo.Commit));
			Assert.That(response.BuildTimestamp, Is.EqualTo(HostBuildInfo.BuildTimestamp));
			Assert.That(response.BuildNumber, Is.EqualTo(HostBuildInfo.BuildNumber));
		});
	}
}
