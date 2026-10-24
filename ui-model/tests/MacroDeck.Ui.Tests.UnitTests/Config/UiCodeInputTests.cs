using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

[TestFixture]
public class UiCodeInputTests
{
	private static readonly string[] _publishedLanguages =
		["text", "json", "liquid", "autohotkey"];

	private static UiSurface ConfigSurface()
	{
		return new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };
	}

	private static UiNode Node(UiView view, string id)
	{
		return view.Tree.Root.Id == id ? view.Tree.Root : view.Tree.Root.Children.Single(child => child.Id == id);
	}

	[Test]
	public void The_published_code_languages_keep_their_values()
	{
		Assert.That(UiCodeLanguages.WellKnown, Is.EqualTo(_publishedLanguages).AsCollection);
	}

	[Test]
	public void A_code_input_written_before_insert_formats_and_content_resources_emits_neither()
	{
		using var view = new UiView(ConfigSurface(), new UiCodeInput { Key = "script", Language = "lua" });

		var properties = Node(view, "script").Properties;

		Assert.Multiple(() =>
		{
			Assert.That(properties.Keys, Does.Not.Contain("variableInsertFormat"));
			Assert.That(properties.Keys, Does.Not.Contain("contentResource"));
			Assert.That(properties.Keys, Does.Not.Contain("rows"));
			Assert.That(properties["language"].GetString(), Is.EqualTo("lua"));
		});
	}

	[Test]
	public void The_insert_format_and_the_content_resource_travel_under_their_published_keys()
	{
		using var view = new UiView(ConfigSurface(),
			new UiCodeInput
			{
				Key = "script",
				Language = UiCodeLanguages.AutoHotkey,
				VariableInsertFormat = "MacroDeck.GetVar(\"{name}\")",
				ContentResource = new UiResource
				{
					ResourceId = "app.example.ahk-0123", ContentHash = "sha256:00", MediaType = "text/plain"
				}
			});

		var properties = Node(view, "script").Properties;

		Assert.Multiple(() =>
		{
			Assert.That(properties["variableInsertFormat"].GetString(), Is.EqualTo("MacroDeck.GetVar(\"{name}\")"));
			Assert.That(properties["contentResource"].GetProperty("resourceId").GetString(),
				Is.EqualTo("app.example.ahk-0123"));
			Assert.That(properties["language"].GetString(), Is.EqualTo("autohotkey"));
		});
	}

	[Test]
	public void Rows_travel_as_the_editor_s_line_count()
	{
		using var view = new UiView(ConfigSurface(), new UiCodeInput { Key = "script", Rows = 22 });

		Assert.That(Node(view, "script").Properties["rows"].GetInt32(), Is.EqualTo(22));
	}

	[Test]
	public async Task An_uploaded_event_hands_the_upload_description_to_the_handler()
	{
		UiContentUpload? received = null;
		using var view = new UiView(ConfigSurface(),
			new UiCodeInput
			{
				Key = "script",
				Events =
				[
					UiCodeInput.OnContentUploaded((upload, _) =>
					{
						received = upload;
						return Task.CompletedTask;
					})
				]
			});

		var result = view.Dispatch(new UiEvent
		{
			NodeId = "script",
			Name = "content-uploaded",
			Data = JsonDocument.Parse("""{"uploadId":"abc","length":12,"contentHash":"sha256:ff","extra":1}""")
				.RootElement
		});
		await view.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(received,
				Is.EqualTo(new UiContentUpload { UploadId = "abc", Length = 12, ContentHash = "sha256:ff" }));
			Assert.That(Node(view, "script").Properties["events"].EnumerateArray().Select(name => name.GetString()),
				Does.Contain("content-uploaded"));
		});
	}

	[TestCase("""{"length":12,"contentHash":"sha256:ff"}""")]
	[TestCase("""{"uploadId":"abc","length":-1,"contentHash":"sha256:ff"}""")]
	[TestCase("""{"uploadId":"abc","length":12}""")]
	[TestCase("\"abc\"")]
	public async Task An_uploaded_event_that_describes_no_upload_never_reaches_the_handler(string payload)
	{
		var called = false;
		using var view = new UiView(ConfigSurface(),
			new UiCodeInput
			{
				Key = "script",
				Events =
				[
					UiCodeInput.OnContentUploaded((_, _) =>
					{
						called = true;
						return Task.CompletedTask;
					})
				]
			});

		view.Dispatch(new UiEvent
		{
			NodeId = "script", Name = "content-uploaded", Data = JsonDocument.Parse(payload).RootElement
		});
		await view.WhenIdleAsync();

		Assert.That(called, Is.False);
	}

	[Test]
	public void A_failed_upload_reaches_its_handler()
	{
		var failed = false;
		using var view = new UiView(ConfigSurface(),
			new UiCodeInput { Key = "script", Events = [UiCodeInput.OnContentUploadFailed(() => failed = true)] });

		var result = view.Dispatch(new UiEvent { NodeId = "script", Name = "content-upload-failed" });

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True);
			Assert.That(failed, Is.True);
		});
	}
}
