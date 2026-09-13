using System.Net;
using System.Text;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Infrastructure.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

public class ConnectZitadelTests
{
	private const string ProjectId = "390578110567413087";
	private const string Picture = "https://auth.macro-deck.app/assets/v1/org-1/users/user-1/avatar";

	[TestCase("Errors.User.RefreshToken.Invalid")]
	[TestCase("Errors.OIDCSession.RefreshTokenInvalid")]
	[TestCase("Errors.User.NotActive")]
	public void A_dead_credential_ends_the_session(string key)
	{
		using var client = Client(new StubHandler(HttpStatusCode.BadRequest, Error("invalid_request", key)));

		Assert.ThrowsAsync<ConnectAuthRejectedException>(() => client.Refresh("refresh-1", CancellationToken.None));
	}

	[TestCase(HttpStatusCode.BadRequest, "invalid_request", "Errors.Internal")]
	[TestCase(HttpStatusCode.BadRequest, "invalid_client", "client_id does not correspond")]
	[TestCase(HttpStatusCode.ServiceUnavailable, "server_error", "Errors.Internal")]
	public void An_outage_on_the_issuer_keeps_the_credential(HttpStatusCode status, string error, string key)
	{
		using var client = Client(new StubHandler(status, Error(error, key)));

		Assert.ThrowsAsync<ConnectAuthTransientException>(() => client.Refresh("refresh-1", CancellationToken.None));
	}

	[Test]
	public async Task A_refresh_rotates_the_token_and_carries_the_profile()
	{
		var idToken = ConnectJwt.Create("user-1",
			"Ada Lovelace",
			Picture,
			"ada",
			audiences: [ConnectEndpoints.ClientId, ProjectId]);
		var handler = new StubHandler(HttpStatusCode.OK,
			$$"""{"access_token":"access-2","token_type":"Bearer","refresh_token":"refresh-2","expires_in":43199,"id_token":"{{idToken}}"}""");
		using var client = Client(handler);

		var tokens = await client.Refresh("refresh-1", CancellationToken.None);
		var claims = ConnectIdTokenReader.Read(tokens.IdToken, null, TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(tokens.RefreshToken, Is.EqualTo("refresh-2"));
			Assert.That(claims.DisplayName, Is.EqualTo("Ada Lovelace"));
			Assert.That(claims.PictureUrl, Is.EqualTo(Picture));
			Assert.That(claims.CreatorUsername, Is.EqualTo("ada"));
			Assert.That(handler.Uris, Is.EqualTo(new[] { "https://auth.macro-deck.app/oauth/v2/token" }));
			Assert.That(handler.Bodies[0], Does.Contain("client_id=390578325090796895"));
			Assert.That(handler.Bodies[0], Does.Contain("grant_type=refresh_token"));
		});
	}

	[Test]
	public async Task A_device_authorization_is_requested_for_this_client()
	{
		var handler = new StubHandler(HttpStatusCode.OK,
			"""{"device_code":"7IpUdn4J5VPlJK9zO6xx9Q","user_code":"DKDK-LSVH","verification_uri":"https://auth.macro-deck.app/device","verification_uri_complete":"https://auth.macro-deck.app/device?user_code=DKDK-LSVH","expires_in":300,"interval":5}""");
		using var client = Client(handler);

		var authorization = await client.RequestDeviceAuthorization(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(authorization.UserCode, Is.EqualTo("DKDK-LSVH"));
			Assert.That(authorization.VerificationUriComplete,
				Is.EqualTo(new Uri("https://auth.macro-deck.app/device?user_code=DKDK-LSVH")));
			Assert.That(authorization.ExpiresIn, Is.EqualTo(TimeSpan.FromSeconds(300)));
			Assert.That(authorization.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
			Assert.That(handler.Uris, Is.EqualTo(new[] { "https://auth.macro-deck.app/oauth/v2/device_authorization" }));
			Assert.That(handler.Bodies[0], Does.Contain("client_id=390578325090796895"));
			Assert.That(handler.Bodies[0], Does.Contain("scope=openid+profile+offline_access"));
		});
	}

	[Test]
	public void An_id_token_issued_for_another_client_is_refused()
	{
		var idToken = ConnectJwt.Create("user-1", audiences: [ProjectId]);

		Assert.Throws<ConnectAuthTransientException>(() => ConnectIdTokenReader.Read(idToken, null, TimeProvider.System));
	}

	[Test]
	public void An_id_token_from_the_previous_issuer_is_refused()
	{
		var idToken = ConnectJwt.Create("user-1", issuer: "https://accounts.macro-deck.app/");

		Assert.Throws<ConnectAuthTransientException>(() => ConnectIdTokenReader.Read(idToken, null, TimeProvider.System));
	}

	private static ConnectIdentityClient Client(StubHandler handler) => new(handler);

	private static string Error(string error, string description)
		=> $$"""{"error":"{{error}}","error_description":"{{description}}"}""";

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _status;
		private readonly string _body;

		public StubHandler(HttpStatusCode status, string body)
		{
			_status = status;
			_body = body;
		}

		public List<string> Uris { get; } = [];

		public List<string> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Uris.Add(request.RequestUri!.ToString());
			Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

			return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
		}
	}
}
