using System.Net;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Auth;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[TestFixture]
public class AuthControllerCookieTests
{
	[Test]
	public async Task Refresh_SetsAccessCookieBeforeRefreshCookie()
	{
		var setCookies = await RefreshAndCaptureCookies(https: false);

		Assert.Multiple(() =>
		{
			Assert.That(setCookies, Has.Count.EqualTo(2));
			Assert.That(setCookies[0], Does.StartWith($"{AuthDefaults.AccessCookieFor(8193)}="));
			Assert.That(setCookies[0], Does.Contain("path=/").IgnoreCase);
			Assert.That(setCookies[1], Does.StartWith($"{AuthDefaults.RefreshCookieFor(8193)}="));
			Assert.That(setCookies[1], Does.Contain("path=/api/auth").IgnoreCase);
		});
	}

	[Test]
	public async Task Auth_cookies_are_marked_secure_only_when_the_request_arrived_over_https()
	{
		var overHttps = await RefreshAndCaptureCookies(https: true);
		var overHttp = await RefreshAndCaptureCookies(https: false);

		Assert.Multiple(() =>
		{
			Assert.That(overHttps, Has.All.Contain("secure").IgnoreCase);
			Assert.That(overHttp, Has.None.Contain("secure").IgnoreCase);
			Assert.That(overHttps, Has.All.Contain("httponly").IgnoreCase);
			Assert.That(overHttps, Has.All.Contain("samesite=strict").IgnoreCase);
		});
	}

	private static async Task<List<string>> RefreshAndCaptureCookies(bool https)
	{
		var login = new LoginResult("access-token",
			DateTime.UtcNow.AddMinutes(15),
			"refresh-token",
			DateTime.UtcNow.AddDays(30),
			AuthScope.Client,
			"manuel");
		var httpContext = new DefaultHttpContext();
		httpContext.Request.Scheme = https ? "https" : "http";
		httpContext.Request.Host = new HostString("192.168.20.13", 8193);
		var controller = new AuthController(new StubAuthService(login),
			new LoginThrottle(TimeProvider.System),
			new PairingCodeStore(),
			TimeProvider.System,
			new UserNotificationStore(),
			new FailedLoginNotificationTracker(),
			TestLocalization.Preferences,
			TestLocalization.Resolver)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext }
		};
		controller.Request.Headers.Cookie = $"{AuthDefaults.RefreshCookieFor(8193)}=raw-token";

		var result = await controller.Refresh();
		Assert.That(result, Is.InstanceOf<OkObjectResult>());

		return controller.Response.Headers.SetCookie.Select(v => v!).ToList();
	}

	[Test]
	public void A_second_host_on_the_same_machine_cannot_take_over_the_cookie()
	{
		// Cookies are scoped to the host name and ignore the port, so an installed host and a development
		// one share a jar. Under a single name the last writer wins and the loser's <img> requests - which
		// cannot carry a header - end up presenting a token it never signed.
		var installed = ContextWithCookies(8193, $"{AuthDefaults.AccessCookieFor(8193)}=installed-token");
		var development = ContextWithCookies(53373, $"{AuthDefaults.AccessCookieFor(8193)}=installed-token");

		Assert.Multiple(() =>
		{
			Assert.That(AuthCookies.ReadAccessToken(installed), Is.EqualTo("installed-token"));
			Assert.That(AuthCookies.ReadAccessToken(development),
				Is.Null,
				"the other installation's cookie must not be mistaken for this one's");
		});
	}

	[Test]
	public void A_session_from_before_the_per_listener_names_keeps_working()
	{
		var upgraded = ContextWithCookies(8193, $"{AuthDefaults.AccessCookie}=legacy-token");

		Assert.That(AuthCookies.ReadAccessToken(upgraded),
			Is.EqualTo("legacy-token"),
			"an upgrade must not sign out everyone who is currently signed in");
	}

	private static DefaultHttpContext ContextWithCookies(int port, string cookieHeader)
	{
		var context = new DefaultHttpContext();
		context.Request.Host = new HostString("192.168.20.13", port);
		context.Request.Headers.Cookie = cookieHeader;

		return context;
	}

	[Test]
	public async Task Failed_login_raises_exactly_one_security_notification_carrying_the_remote_ip()
	{
		var store = new UserNotificationStore();
		var controller = new AuthController(new FailingAuthService(),
			new LoginThrottle(TimeProvider.System),
			new PairingCodeStore(),
			TimeProvider.System,
			store,
			new FailedLoginNotificationTracker(),
			TestLocalization.Preferences,
			TestLocalization.Resolver)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.7") }
				}
			}
		};

		await controller.Login(new LoginRequest("attacker", "wrong-password", AuthDefaults.AdminScope));
		await controller.Login(new LoginRequest("attacker", "wrong-password", AuthDefaults.AdminScope));

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notifications[0].Kind, Is.EqualTo(UserNotificationKind.Security));
			Assert.That(notifications[0].Message, Does.Contain("203.0.113.7"));
			Assert.That(notifications[0].Message, Does.Not.Contain("wrong-password"));
		});
	}

	[Test]
	public async Task Failed_logins_from_different_ips_still_collapse_into_one_notification()
	{
		var store = new UserNotificationStore();
		var controller = new AuthController(new FailingAuthService(),
			new LoginThrottle(TimeProvider.System),
			new PairingCodeStore(),
			TimeProvider.System,
			store,
			new FailedLoginNotificationTracker(),
			TestLocalization.Preferences,
			TestLocalization.Resolver)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.7") }
				}
			}
		};

		await controller.Login(new LoginRequest("attacker", "wrong-password", AuthDefaults.AdminScope));

		controller.ControllerContext.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
		await controller.Login(new LoginRequest("second-attacker", "wrong-password", AuthDefaults.AdminScope));

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(notifications, Has.Count.EqualTo(1), "different source IPs must still collapse into one entry");
			Assert.That(notifications[0].Message, Does.Contain("2 failed attempts"));
			Assert.That(notifications[0].Message, Does.Contain("198.51.100.9"), "must reflect the latest attempt");
			Assert.That(notifications[0].Message, Does.Contain("second-attacker"), "must reflect the latest attempt");
			Assert.That(notifications[0].Message, Does.Not.Contain("203.0.113.7"));
		});
	}

	[Test]
	public async Task The_failed_login_notification_is_written_in_the_language_the_rest_of_the_interface_uses()
	{
		var store = new UserNotificationStore();
		var controller = new AuthController(new FailingAuthService(),
			new LoginThrottle(TimeProvider.System),
			new PairingCodeStore(),
			TimeProvider.System,
			store,
			new FailedLoginNotificationTracker(),
			new FakeLocalizationPreferences { Culture = "de" },
			TestLocalization.Resolver)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.7") }
				}
			}
		};

		await controller.Login(new LoginRequest("attacker", "wrong-password", AuthDefaults.AdminScope));

		var notification = store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Title, Is.EqualTo("Fehlgeschlagener Anmeldeversuch"));
			Assert.That(notification.Message, Does.Contain("1 fehlgeschlagener Versuch"));
			Assert.That(notification.Message, Does.Contain("203.0.113.7"));
		});
	}

	private sealed class StubAuthService : IAuthService
	{
		public Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime)
			=> throw new NotSupportedException();

		public Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(string token, DeviceRegistration device)
			=> throw new NotSupportedException();

		private readonly LoginResult _login;

		public StubAuthService(LoginResult login)
		{
			_login = login;
		}

		public Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken)
			=> Task.FromResult(Result.Ok<LoginResult, AuthError>(_login));

		public Task<bool> IsSetupComplete() => Task.FromResult(true);

		public Task<string?> GetUsername() => Task.FromResult<string?>(_login.Username);

		public Task<Result<AuthError>> Setup(string username, string password)
			=> throw new NotSupportedException();

		public Task<Result<LoginResult, AuthError>> Login(string username,
			string password,
			AuthScope scope,
			DeviceRegistration? device)
			=> throw new NotSupportedException();

		public Task Logout(string rawRefreshToken) => Task.CompletedTask;

		public Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword)
			=> throw new NotSupportedException();

		public Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername)
			=> throw new NotSupportedException();
	}

	private sealed class FailingAuthService : IAuthService
	{
		public Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime)
			=> throw new NotSupportedException();

		public Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(string token, DeviceRegistration device)
			=> throw new NotSupportedException();

		public Task<bool> IsSetupComplete() => Task.FromResult(true);

		public Task<string?> GetUsername() => Task.FromResult<string?>(null);

		public Task<Result<AuthError>> Setup(string username, string password)
			=> throw new NotSupportedException();

		public Task<Result<LoginResult, AuthError>> Login(string username,
			string password,
			AuthScope scope,
			DeviceRegistration? device)
			=> Task.FromResult(Result.Fail<LoginResult, AuthError>(AuthError.InvalidCredentials));

		public Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken)
			=> throw new NotSupportedException();

		public Task Logout(string rawRefreshToken) => throw new NotSupportedException();

		public Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword)
			=> throw new NotSupportedException();

		public Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername)
			=> throw new NotSupportedException();
	}
}
