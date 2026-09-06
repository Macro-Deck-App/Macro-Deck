using MacroDeckHost.Application.Auth;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.ClientTargets;

/// <summary>Only the enrollment half is real; provisioning touches nothing else on this port.</summary>
internal sealed class FakeEnrollmentAuthService : IAuthService
{
	public List<TimeSpan> MintedLifetimes { get; } = [];

	public string TokenToIssue { get; set; } = "enrollment-token";

	public bool MintSucceeds { get; set; } = true;

	public Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime)
	{
		MintedLifetimes.Add(lifetime);
		return Task.FromResult(MintSucceeds
			? Result.Ok<DeviceEnrollmentTicket, AuthError>(new DeviceEnrollmentTicket(TokenToIssue,
				DateTime.UnixEpoch.Add(lifetime)))
			: Result.Fail<DeviceEnrollmentTicket, AuthError>(AuthError.InvalidCredentials, "no account"));
	}

	public Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(string token, DeviceRegistration device)
		=> throw new NotSupportedException();

	public Task<bool> IsSetupComplete() => throw new NotSupportedException();

	public Task<string?> GetUsername() => throw new NotSupportedException();

	public Task<Result<AuthError>> Setup(string username, string password) => throw new NotSupportedException();

	public Task<Result<LoginResult, AuthError>> Login(string username,
		string password,
		AuthScope scope,
		DeviceRegistration? device = null)
		=> throw new NotSupportedException();

	public Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken) => throw new NotSupportedException();

	public Task Logout(string rawRefreshToken) => throw new NotSupportedException();

	public Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword)
		=> throw new NotSupportedException();

	public Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername)
		=> throw new NotSupportedException();
}
