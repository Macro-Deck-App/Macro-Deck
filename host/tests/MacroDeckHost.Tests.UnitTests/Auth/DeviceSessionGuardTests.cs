using System.Globalization;
using System.Security.Claims;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class DeviceSessionGuardTests
{
	private static readonly DateTime _signedOutAt = new(2026, 9, 13, 12, 0, 0, 500, DateTimeKind.Utc);

	private static readonly Guid _device = Guid.Parse("11111111-1111-1111-1111-111111111111");

	[Test]
	public void A_token_without_a_device_is_none_of_the_guards_business()
	{
		var guard = new DeviceSessionGuard();

		Assert.That(guard.Rejects(new ClaimsPrincipal(new ClaimsIdentity())), Is.False);
	}

	[Test]
	public void A_tracked_device_that_was_never_signed_out_is_accepted()
	{
		var guard = new DeviceSessionGuard();
		guard.Track(_device);

		Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt)), Is.False);
	}

	[Test]
	public void A_device_the_guard_does_not_know_is_refused()
	{
		var guard = new DeviceSessionGuard();
		guard.Seed([(Guid.NewGuid(), null)]);

		Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt)), Is.True);
	}

	[Test]
	public void A_deleted_device_stops_being_accepted()
	{
		var guard = new DeviceSessionGuard();
		guard.Track(_device);
		var before = guard.Rejects(TokenFor(_device, _signedOutAt));

		guard.Forget(_device);

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.False);
			Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt)), Is.True);
		});
	}

	[Test]
	public void Only_tokens_issued_up_to_the_sign_out_second_are_refused()
	{
		var guard = new DeviceSessionGuard();
		guard.Track(_device);
		guard.Revoke(_device, _signedOutAt);

		Assert.Multiple(() =>
		{
			Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt.AddMinutes(-10))), Is.True);
			Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt)), Is.True);
			Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt.AddSeconds(1))), Is.False);
		});
	}

	[Test]
	public void A_device_signing_in_again_in_the_same_second_gets_a_usable_token()
	{
		var guard = new DeviceSessionGuard();
		guard.Track(_device);
		guard.Revoke(_device, _signedOutAt);

		var issuedAt = guard.IssuedAtFor(_device, _signedOutAt);

		Assert.That(guard.Rejects(TokenFor(_device, issuedAt)), Is.False);
	}

	[Test]
	public void A_revoked_device_presenting_a_token_the_host_cannot_date_is_refused()
	{
		var guard = new DeviceSessionGuard();
		guard.Track(_device);
		guard.Revoke(_device, _signedOutAt);

		var undated = new ClaimsPrincipal(new ClaimsIdentity([
			new Claim(AuthDefaults.DeviceClaim, _device.ToString())
		]));

		Assert.That(guard.Rejects(undated), Is.True);
	}

	[Test]
	public void Seeding_restores_a_sign_out_that_happened_before_this_host_started()
	{
		var guard = new DeviceSessionGuard();
		guard.Seed([(_device, _signedOutAt)]);

		Assert.That(guard.Rejects(TokenFor(_device, _signedOutAt.AddMinutes(-1))), Is.True);
	}

	private static ClaimsPrincipal TokenFor(Guid deviceId, DateTime issuedAt)
	{
		return new ClaimsPrincipal(new ClaimsIdentity([
			new Claim(AuthDefaults.DeviceClaim, deviceId.ToString()),
			new Claim(AccessTokenIssuedAt.Claim,
				AccessTokenIssuedAt.UnixSeconds(issuedAt).ToString(CultureInfo.InvariantCulture))
		]));
	}
}
