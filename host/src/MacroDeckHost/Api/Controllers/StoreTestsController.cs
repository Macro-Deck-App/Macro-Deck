using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/store/tests")]
public class StoreTestsController : ControllerBase
{
	private readonly IStoreTestService _tests;

	public StoreTestsController(IStoreTestService tests)
	{
		_tests = tests;
	}

	[HttpGet]
	public async Task<GetStoreTestsResponse> GetTests(CancellationToken ct)
	{
		var result = await _tests.GetTests(ct);
		if (!result.Success)
		{
			return new GetStoreTestsResponse { Success = false, Error = ToError(result.Failure) };
		}

		return new GetStoreTestsResponse
		{
			Success = true,
			Tests = result.Tests
				.Select(test => new StoreTestBody
				{
					PackageId = test.Test.PackageId,
					DisplayName = test.Test.DisplayName,
					JoinedAt = test.Test.JoinedAt,
					HasIcon = test.HasIcon,
					IconSha256 = test.IconSha256,
					InstalledVersion = test.InstalledVersion,
					InstalledTestBuildId = test.InstalledTestBuildId,
					StoreVersion = test.StoreVersion,
					ActiveOperationId = test.ActiveOperationId,
					Builds = test.Test.Builds
						.Select(build => new StoreTestBuildBody
						{
							Id = build.Id,
							Version = build.Version,
							Build = build.Build,
							Changelog = build.Changelog,
							SizeInBytes = build.SizeInBytes,
							UploadedAt = build.UploadedAt,
							AvailableAt = build.AvailableAt
						})
						.ToList()
				})
				.ToList()
		};
	}

	[HttpPost("install")]
	public async Task<StoreOperationActionResponse> Install(InstallStoreTestBuildRequest body, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(body.PackageId) || body.BuildId == Guid.Empty)
		{
			return new StoreOperationActionResponse
			{
				Success = false,
				Error = new TransportError { Code = "invalid_request", Message = "A package id and a build id are required." }
			};
		}

		if (!body.Consent)
		{
			return new StoreOperationActionResponse
			{
				Success = false,
				Error = new TransportError { Code = "consent_required", Message = "Installing a test build needs confirmation." }
			};
		}

		var result = await _tests.Install(body.PackageId, body.BuildId, body.Consent, ct);
		return result.Success
			? new StoreOperationActionResponse { Success = true, Operation = StoreOperationBodyFactory.Create(result.Operation!) }
			: new StoreOperationActionResponse { Success = false, Error = ToError(result.Failure) };
	}

	private static TransportError ToError(StorePlatformFailure failure) => failure switch
	{
		StorePlatformFailure.SignInRequired => new TransportError { Code = "sign_in_required", Message = "Sign in to see your tests." },
		StorePlatformFailure.AccountSuspended => new TransportError { Code = "account_suspended", Message = "This account is suspended." },
		StorePlatformFailure.NotFound => new TransportError { Code = "not_found", Message = "The test build is no longer available." },
		StorePlatformFailure.Cooldown => new TransportError { Code = "rate_limited", Message = "Too many requests. Try again shortly." },
		_ => new TransportError { Code = "unavailable", Message = "The Macro Deck Platform could not be reached." }
	};
}
