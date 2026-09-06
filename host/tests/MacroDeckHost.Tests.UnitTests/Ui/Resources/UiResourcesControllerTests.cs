using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
public class UiResourcesControllerTests
{
	private const string _svg = """<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>""";

	private static UiResourceStore StoreWith(out string resourceId, string name = "clear-day")
	{
		var store = new UiResourceStore();

		var handle = store.Register(new UiResourceRegistration
		{
			OwnerId = "app.macro-deck.weather",
			Name = name,
			MediaType = "image/svg+xml",
			Content = Encoding.UTF8.GetBytes(_svg),
		});

		resourceId = handle.ResourceId;

		return store;
	}

	private static UiResourcesController Controller(IUiResourceStore store, string? ifNoneMatch = null)
	{
		var httpContext = new DefaultHttpContext();

		if (ifNoneMatch is not null)
		{
			httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;
		}

		return new UiResourcesController(store)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};
	}

	[Test]
	public void The_endpoint_requires_client_access()
	{
		var policy = typeof(UiResourcesController)
			.GetMethod(nameof(UiResourcesController.Get))!
			.GetCustomAttribute<AuthorizeAttribute>()
			?.Policy;

		Assert.That(policy, Is.EqualTo(AuthPolicies.ClientAccess));
	}

	[Test]
	public void A_served_resource_cannot_be_sniffed_or_run_as_a_document()
	{
		var store = StoreWith(out var resourceId);
		var controller = Controller(store);

		controller.Get(resourceId);

		Assert.Multiple(() =>
		{
			Assert.That(controller.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
			Assert.That(controller.Response.Headers.ContentSecurityPolicy.ToString(),
				Does.Contain("default-src 'none'"));
			Assert.That(controller.Response.Headers.ContentSecurityPolicy.ToString(), Does.Contain("sandbox"));
		});
	}

	[Test]
	public void A_registered_resource_is_served_with_its_media_type_and_a_strong_hash_etag()
	{
		var store = StoreWith(out var resourceId);
		var controller = Controller(store);

		var result = controller.Get(resourceId) as FileContentResult;

		Assert.That(result, Is.Not.Null);

		var expected = $"\"sha256:{Convert.ToHexStringLower(SHA256.HashData(result!.FileContents))}\"";

		Assert.Multiple(() =>
		{
			Assert.That(result.ContentType, Is.EqualTo("image/svg+xml"));
			Assert.That(Encoding.UTF8.GetString(result.FileContents), Is.EqualTo(_svg));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo(expected));
			Assert.That(controller.Response.Headers.ETag.ToString(),
				Does.Not.StartWith("W/"),
				"a weak validator would let a client keep a byte-different copy");
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Does.Contain("private"));
		});
	}

	[Test]
	public void A_matching_if_none_match_is_answered_with_304_and_no_body()
	{
		var store = StoreWith(out var resourceId);
		var etag = $"\"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_svg)))}\"";
		var controller = Controller(store, etag);

		var result = controller.Get(resourceId);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<StatusCodeResult>());
			Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo(etag));
		});
	}

	[Test]
	public void A_stale_if_none_match_is_answered_with_the_full_body()
	{
		var store = StoreWith(out var resourceId);
		var controller = Controller(store, $"\"sha256:{new string('0', 64)}\"");

		Assert.That(controller.Get(resourceId), Is.InstanceOf<FileContentResult>());
	}

	[TestCase("app.macro-deck.weather.does-not-exist")]
	[TestCase("../../appsettings.json")]
	[TestCase("/etc/passwd")]
	[TestCase("")]
	public void An_unknown_or_unusable_id_is_answered_the_same_way(string resourceId)
	{
		var controller = Controller(StoreWith(out _));

		var result = controller.Get(resourceId);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
		});
	}
}
