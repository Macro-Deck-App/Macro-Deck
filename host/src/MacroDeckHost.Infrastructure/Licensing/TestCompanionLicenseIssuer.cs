using System.Security.Cryptography;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Licensing;

public sealed class TestCompanionLicenseIssuer
{
	public const string TestSource = "test";

	// Test key only: hosts trust it in developer mode and Companions in debug builds, nowhere else.
	private const string TestPrivateKeyPem = """
		-----BEGIN PRIVATE KEY-----
		MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg3YdVZblUi2WgMWR5
		d8cvpwfE0pqjDpTNy2mFdENI6KehRANCAATHRT0yoORyVwLFQoDTde9TsghqK7MJ
		Yae8fIpDf7HeSC1ztGi+zVl7Gh+WUCqnmmc46l2q8+TCINOp4JRNR3nM
		-----END PRIVATE KEY-----
		""";

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;

	public TestCompanionLicenseIssuer(IServiceScopeFactory scopeFactory, TimeProvider time)
	{
		_scopeFactory = scopeFactory;
		_time = time;
	}

	public async Task<string?> IssueAsync(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		if (!(await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetDeveloper()).Enabled)
		{
			return null;
		}

		var now = _time.GetUtcNow();
		using var key = ECDsa.Create();
		key.ImportFromPem(TestPrivateKeyPem);
		return CompanionLicenseTokens.Sign(key,
			CompanionLicenseTokens.TestKeyId,
			Guid.NewGuid().ToString("N"),
			TestSource,
			now,
			now);
	}
}
