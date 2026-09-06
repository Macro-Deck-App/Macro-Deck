using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[TestFixture]
public class ConfigureJwtBearerOptionsTests
{
	private JwtBearerOptions _options = null!;

	[SetUp]
	public void SetUp()
	{
		_options = new JwtBearerOptions();
		new ConfigureJwtBearerOptions(new StubSigningKeyProvider()).Configure(_options);
	}

	[Test]
	public async Task Challenge_MarksResponseUncacheable()
	{
		var context = new JwtBearerChallengeContext(new DefaultHttpContext(),
			new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
			_options,
			new AuthenticationProperties());

		await _options.Events.OnChallenge(context);

		Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
	}

	[Test]
	public async Task Forbidden_MarksResponseUncacheable()
	{
		var context = new ForbiddenContext(new DefaultHttpContext(),
			new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
			_options);

		await _options.Events.OnForbidden(context);

		Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
	}

	private sealed class StubSigningKeyProvider : ISigningKeyProvider
	{
		private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

		public byte[] GetKey() => _key;
	}
}
