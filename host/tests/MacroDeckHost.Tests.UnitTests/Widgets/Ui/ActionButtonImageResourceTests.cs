using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Widgets.ActionButton;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

[TestFixture]
public class ActionButtonImageResourceTests
{
	[Test]
	public void A_valid_base64_data_uri_decodes_to_its_exact_bytes_and_declared_media_type()
	{
		var bytes = new byte[] { 1, 2, 3, 4, 250, 251, 252, 253 };
		var dataUri = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

		var parsed = ActionButtonImageResource.TryParse(dataUri, out var content, out var mediaType);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(content.ToArray(), Is.EqualTo(bytes));
			Assert.That(mediaType, Is.EqualTo("image/png"));
		});
	}

	[Test]
	public void An_http_or_https_url_is_rejected_so_the_host_never_fetches_a_stored_url_on_session_open()
	{
		// Issue #748's resolved security decision: the legacy imageUrl field may hold an http(s) URL, but a
		// widget tree must never cause the host to fetch an arbitrary stored URL every time a session opens -
		// so only a data: URI (already-local bytes) is ever decoded, and an http(s) URL is ignored outright.
		var http = ActionButtonImageResource.TryParse("http://example.com/icon.png",
			out var httpContent,
			out var httpMediaType);
		var https = ActionButtonImageResource.TryParse("https://example.com/icon.png",
			out var httpsContent,
			out var httpsMediaType);

		Assert.Multiple(() =>
		{
			Assert.That(http, Is.False);
			Assert.That(httpContent.IsEmpty, Is.True);
			Assert.That(httpMediaType, Is.Empty);
			Assert.That(https, Is.False);
			Assert.That(httpsContent.IsEmpty, Is.True);
			Assert.That(httpsMediaType, Is.Empty);
		});
	}

	[Test]
	public void Content_at_the_protocol_max_ui_resource_bytes_limit_is_accepted()
	{
		var bytes = new byte[ProtocolLimits.MaxUiResourceBytes];
		Array.Fill(bytes, (byte)7);
		var dataUri = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

		var parsed = ActionButtonImageResource.TryParse(dataUri, out var content, out _);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(content.Length, Is.EqualTo(ProtocolLimits.MaxUiResourceBytes));
		});
	}

	[Test]
	public void Content_one_byte_over_the_protocol_max_ui_resource_bytes_limit_is_rejected()
	{
		var bytes = new byte[ProtocolLimits.MaxUiResourceBytes + 1];
		Array.Fill(bytes, (byte)7);
		var dataUri = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

		var parsed = ActionButtonImageResource.TryParse(dataUri, out var content, out var mediaType);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.False);
			Assert.That(content.IsEmpty, Is.True);
			Assert.That(mediaType, Is.Empty);
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("not a data uri at all")]
	[TestCase("data:image/png;base64,not-valid-base64!!!")]
	[TestCase("data:image/png;base64SGVsbG8=")] // no comma separating header from payload
	[TestCase("data:image/png;name=icon.png,SGVsbG8=")] // declared, but never marked ;base64
	public void Malformed_or_non_base64_input_degrades_to_no_artwork_instead_of_throwing(string? imageUrl)
	{
		var parsed = false;

		Assert.DoesNotThrow(() => parsed = ActionButtonImageResource.TryParse(imageUrl, out _, out _));
		Assert.That(parsed, Is.False);
	}
}
