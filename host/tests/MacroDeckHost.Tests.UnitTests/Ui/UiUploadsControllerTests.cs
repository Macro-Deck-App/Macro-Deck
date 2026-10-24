using System.Reflection;
using System.Security.Claims;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class UiUploadsControllerTests
{
	private const string SessionId = "session-1";

	private UiSessionRegistry _sessions = null!;
	private UiUploadStore _uploads = null!;

	[SetUp]
	public void SetUp()
	{
		_sessions = new UiSessionRegistry(TimeProvider.System);
		_sessions.Create(SessionId,
			"com.example.pages",
			new UiSurface { Kind = UiSurfaceKinds.Page, SessionMode = UiSessionModes.Exclusive },
			string.Empty);
		_uploads = new UiUploadStore(_sessions, TimeProvider.System);
	}

	[TearDown]
	public void TearDown()
	{
		_uploads.Dispose();
		_sessions.Dispose();
	}

	[Test]
	public void Uploading_requires_an_admin_and_is_limited_to_two_mebibytes()
	{
		var method = typeof(UiUploadsController).GetMethod(nameof(UiUploadsController.Upload))!;

		Assert.Multiple(() =>
		{
			Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()?.Policy, Is.EqualTo(AuthPolicies.Admin));
			Assert.That((method.GetCustomAttribute<RequestSizeLimitAttribute>() as IRequestSizeLimitMetadata)
				?.MaxRequestBodySize,
				Is.EqualTo(2 * 1024 * 1024));
		});
	}

	[Test]
	public async Task The_session_owner_gets_an_upload_id_and_the_hash_macro_deck_computed()
	{
		var text = "MsgBox \"äöü\""u8.ToArray();

		var result = await Controller(text).Upload(SessionId, "editor", CancellationToken.None) as OkObjectResult;

		var response = (UiUploadResponse)result!.Value!;
		Assert.Multiple(() =>
		{
			Assert.That(response.Length, Is.EqualTo(text.Length));
			Assert.That(response.ContentHash, Is.EqualTo(AssetContentHash.Compute(text)));
			Assert.That(_uploads.Read("com.example.pages", SessionId, response.UploadId)!.Content.ToArray(),
				Is.EqualTo(text));
		});
	}

	[Test]
	public async Task A_caller_that_does_not_own_the_session_is_told_it_does_not_exist()
	{
		var result = await Controller("x"u8.ToArray(), Guid.NewGuid().ToString())
			.Upload(SessionId, "editor", CancellationToken.None);

		Assert.That(result, Is.InstanceOf<NotFoundResult>());
	}

	[Test]
	public async Task A_body_over_two_mebibytes_is_refused_without_being_stored()
	{
		var result = await Controller(new byte[UiUploadsController.MaxUploadBytes + 1])
			.Upload(SessionId, "editor", CancellationToken.None);

		Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
	}

	private UiUploadsController Controller(byte[] body, string? deviceId = null)
	{
		Claim[] claims = deviceId is null ? [] : [new Claim(AuthDefaults.DeviceClaim, deviceId)];
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
			Request =
			{
				Body = new MemoryStream(body)
			}
		};

		return new UiUploadsController(_uploads)
		{
			ControllerContext = new ControllerContext { HttpContext = context }
		};
	}
}
