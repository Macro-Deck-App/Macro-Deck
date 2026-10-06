using MacroDeckHost.Application.Applications;
using MacroDeckHost.Infrastructure.Applications;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class ExternalUrlOpenerTests
{
	[Test]
	public void A_web_link_opens_on_the_host()
	{
		var applications = new FakeApplicationService();
		var opener = new ExternalUrlOpener(new FakeHostLockState(), applications);

		var result = opener.Open("https://meet.example.com/abc-defg");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(applications.OpenedWebsites, Is.EqualTo(new[] { "https://meet.example.com/abc-defg" }));
		});
	}

	[Test]
	public void Nothing_opens_while_the_host_is_locked()
	{
		var applications = new FakeApplicationService();
		var opener = new ExternalUrlOpener(new FakeHostLockState { IsLocked = true }, applications);

		var result = opener.Open("https://meet.example.com/abc-defg");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(ExternalUrlOpenError.HostLocked));
			Assert.That(applications.OpenedWebsites, Is.Empty);
		});
	}

	[TestCase("javascript:alert(1)")]
	[TestCase("file:///etc/passwd")]
	[TestCase("zoommtg://zoom.us/join?confno=1")]
	[TestCase("/relative/path")]
	[TestCase("")]
	public void Anything_but_an_absolute_web_link_is_refused(string url)
	{
		var applications = new FakeApplicationService();
		var opener = new ExternalUrlOpener(new FakeHostLockState(), applications);

		var result = opener.Open(url);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(ExternalUrlOpenError.InvalidUrl));
			Assert.That(applications.OpenedWebsites, Is.Empty);
		});
	}
}
