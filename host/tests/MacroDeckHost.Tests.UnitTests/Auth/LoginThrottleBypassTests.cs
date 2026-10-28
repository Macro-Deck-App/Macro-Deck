using System.Net;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[TestFixture]
public class LoginThrottleBypassTests
{
	private const string Username = "admin";
	private const string Password = "correct horse";
	private static readonly IPAddress _attacker = IPAddress.Parse("203.0.113.7");

	private LoginThrottle _throttle = null!;
	private GatedAuthService _auth = null!;

	[SetUp]
	public void SetUp()
	{
		_throttle = new LoginThrottle(TimeProvider.System);
		_auth = new GatedAuthService();
	}

	[Test]
	public async Task Whitespace_and_case_variants_of_the_username_share_one_set_of_attempts()
	{
		string[] variants = ["admin", "admin ", " admin", "ADMIN\t", "Admin  "];
		foreach (var variant in variants)
		{
			await Login(_attacker, variant, "wrong");
		}

		var next = await Login(_attacker, "admin   ", "wrong");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(StatusOf(next.Result), Is.EqualTo(StatusCodes.Status429TooManyRequests));
			Assert.That(next.RetryAfter, Is.Not.Null.And.Not.Empty);
			Assert.That(_auth.Attempts, Is.EqualTo(5));
			Assert.That(StatusOf((await Login(_attacker, Username, Password)).Result),
				Is.EqualTo(StatusCodes.Status429TooManyRequests));
		});
	}

	[Test]
	public async Task A_parallel_burst_reaches_the_password_check_no_more_than_five_times()
	{
		_auth.Hold();
		var burst = Enumerable.Range(0, 20)
			.Select(_ => Login(_attacker, Username, "wrong"))
			.ToArray();
		await _auth.WaitForAttempts(5);
		await Task.Delay(100);
		_auth.Release();
		var results = await Task.WhenAll(burst);

		Assert.Multiple(() =>
		{
			Assert.That(_auth.Attempts, Is.EqualTo(5));
			Assert.That(results.Count(r => StatusOf(r.Result) == StatusCodes.Status429TooManyRequests),
				Is.EqualTo(15));
		});
	}

	[Test]
	public async Task Different_usernames_from_one_source_share_its_five_attempts()
	{
		foreach (var name in new[] { "root", "administrator", "manuel", "user", "macrodeck" })
		{
			await Login(_attacker, name, "wrong");
		}

		var next = await Login(_attacker, Username, "wrong");

		Assert.Multiple(() =>
		{
			Assert.That(StatusOf(next.Result), Is.EqualTo(StatusCodes.Status429TooManyRequests));
			Assert.That(_auth.Attempts, Is.EqualTo(5));
		});
	}

	[Test]
	public async Task A_locked_out_source_does_not_lock_out_another_source()
	{
		for (var attempt = 0; attempt < 10; attempt++)
		{
			await Login(_attacker, Username, "wrong");
		}

		var other = await Login(IPAddress.Parse("192.168.20.30"), Username, Password);

		Assert.That(StatusOf(other.Result), Is.EqualTo(StatusCodes.Status200OK));
	}

	[Test]
	public async Task A_successful_login_clears_the_failures_counted_before_it()
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			await Login(_attacker, Username, "wrong");
		}

		var success = await Login(_attacker, $" {Username} ", Password);
		for (var attempt = 0; attempt < 4; attempt++)
		{
			await Login(_attacker, Username, "wrong");
		}

		var afterFourMore = await Login(_attacker, Username, Password);

		Assert.Multiple(() =>
		{
			Assert.That(StatusOf(success.Result), Is.EqualTo(StatusCodes.Status200OK));
			Assert.That(StatusOf(afterFourMore.Result), Is.EqualTo(StatusCodes.Status200OK));
		});
	}

	private async Task<(IActionResult Result, string? RetryAfter)> Login(
		IPAddress source,
		string username,
		string password)
	{
		var httpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = source } };
		var controller = new AuthController(_auth,
			_throttle,
			new PairingCodeStore(),
			TimeProvider.System,
			new UserNotificationStore(),
			new FailedLoginNotificationTracker(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			new NoHostIdentity())
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext }
		};

		var result = await controller.Login(new LoginRequest(username, password, AuthDefaults.AdminScope));
		return (result, httpContext.Response.Headers.RetryAfter.ToString());
	}

	private static int? StatusOf(IActionResult result)
	{
		return result switch
		{
			ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
			IStatusCodeActionResult statusResult => statusResult.StatusCode,
			_ => null
		};
	}

	private sealed class GatedAuthService : IAuthService
	{
		private readonly Lock _lock = new();
		private TaskCompletionSource _gate = CompletedGate();
		private TaskCompletionSource _attemptsReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _awaitedAttempts = int.MaxValue;
		private int _attempts;

		public int Attempts => Volatile.Read(ref _attempts);

		public void Hold()
		{
			_gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		public void Release()
		{
			_gate.TrySetResult();
		}

		public Task WaitForAttempts(int count)
		{
			lock (_lock)
			{
				_awaitedAttempts = count;
				if (_attempts >= count)
				{
					_attemptsReached.TrySetResult();
				}
			}

			return _attemptsReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}

		public async Task<Result<LoginResult, AuthError>> Login(
			string username,
			string password,
			AuthScope scope,
			DeviceRegistration? device)
		{
			lock (_lock)
			{
				if (++_attempts >= _awaitedAttempts)
				{
					_attemptsReached.TrySetResult();
				}
			}

			await _gate.Task;

			if (!string.Equals(username.Trim(), Username, StringComparison.OrdinalIgnoreCase)
				|| password != Password)
			{
				return Result.Fail<LoginResult, AuthError>(AuthError.InvalidCredentials);
			}

			return Result.Ok<LoginResult, AuthError>(new LoginResult("access-token",
				DateTime.UtcNow.AddMinutes(15),
				"refresh-token",
				DateTime.UtcNow.AddDays(30),
				scope,
				Username));
		}

		public Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime)
		{
			throw new NotSupportedException();
		}

		public Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(string token, DeviceRegistration device)
		{
			throw new NotSupportedException();
		}

		public Task<bool> IsSetupComplete()
		{
			return Task.FromResult(true);
		}

		public Task<string?> GetUsername()
		{
			return Task.FromResult<string?>(Username);
		}

		public Task<Result<AuthError>> Setup(string username, string password)
		{
			throw new NotSupportedException();
		}

		public Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken)
		{
			throw new NotSupportedException();
		}

		public Task Logout(string rawRefreshToken)
		{
			throw new NotSupportedException();
		}

		public Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword)
		{
			throw new NotSupportedException();
		}

		public Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername)
		{
			throw new NotSupportedException();
		}

		public Task<Result<AuthError>> ResetPassword(string newPassword)
		{
			throw new NotSupportedException();
		}

		private static TaskCompletionSource CompletedGate()
		{
			var gate = new TaskCompletionSource();
			gate.SetResult();
			return gate;
		}
	}

	private sealed class NoHostIdentity : IHostIdentityKeyProvider
	{
		public ValueTask<byte[]> GetPublicKey(CancellationToken cancellationToken = default)
		{
			return ValueTask.FromException<byte[]>(new HostIdentityUnavailableException("none"));
		}

		public ValueTask<byte[]> Sign(byte[] message, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromException<byte[]>(new HostIdentityUnavailableException("none"));
		}
	}
}
