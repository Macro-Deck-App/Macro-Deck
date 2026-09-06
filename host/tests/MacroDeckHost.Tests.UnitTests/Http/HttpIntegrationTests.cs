using MacroDeckHost.Integrations.Http;
using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpIntegrationTests
{
	private static readonly string[] _actionIds = ["send-request", "get-json-value"];

	private static readonly string[] _methods =
		["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "QUERY"];

	private HttpIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_integration = new HttpIntegration(new FakeHttpRequestClient());
	}

	[Test]
	public void The_integration_reports_its_id_name_and_version()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.http"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.EqualTo("HTTP"));
			Assert.That(_integration.Version, Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public void It_declares_exactly_the_two_documented_actions()
	{
		Assert.That(_integration.Actions.Select(action => action.Id), Is.EquivalentTo(_actionIds));
	}

	[Test]
	public void Parameter_names_are_unique_within_each_action()
	{
		Assert.Multiple(() =>
		{
			foreach (var action in _integration.Actions)
			{
				var names = action.Parameters.Select(parameter => parameter.Name).ToList();
				Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count), $"duplicate parameter on {action.Id}");
			}
		});
	}

	[Test]
	public void Url_is_the_only_required_parameter_on_send_request()
	{
		var sendRequest = _integration.Actions.Single(action => action.Id == "send-request");

		var required = sendRequest.Parameters.Where(parameter => parameter.Required).Select(p => p.Name).ToList();

		Assert.That(required, Is.EqualTo(new[] { HttpRequestBuilder.UrlParameter }));
	}

	[Test]
	public void Get_icon_returns_non_empty_svg_bytes()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.GetIcon(), Is.Not.Empty);
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
		});
	}

	[Test]
	public void The_integration_implements_no_config_flow_so_it_is_enabled_by_default()
	{
		Assert.That(_integration, Is.Not.InstanceOf<IConfigFlowProvider>());
	}

	[Test]
	public void The_method_parameter_is_a_dropdown_offering_query()
	{
		var method = SendRequestParameter("method");

		Assert.Multiple(() =>
		{
			Assert.That(method.Type, Is.EqualTo(ActionParameterType.Choice));
			Assert.That(method.DefaultValue, Is.EqualTo("GET"));
			Assert.That(method.Options!.Select(option => option.Value), Is.EqualTo(_methods));
		});
	}

	[TestCase("authUsername", "authType", "basic")]
	[TestCase("authSecret", "authType", "basic,bearer,header")]
	[TestCase("authHeaderName", "authType", "header")]
	[TestCase("jsonBody", "bodyType", "json")]
	[TestCase("formBody", "bodyType", "form,multipart")]
	[TestCase("textBody", "bodyType", "text")]
	[TestCase("filePath", "bodyType", "multipart")]
	[TestCase("fileFieldName", "bodyType", "multipart")]
	[TestCase("contentType", "bodyType", "json,form,text")]
	public void Mode_specific_parameters_are_only_visible_for_their_mode(
		string parameterName,
		string dependsOn,
		string values)
	{
		var visibility = SendRequestParameter(parameterName).VisibleWhen;

		Assert.Multiple(() =>
		{
			Assert.That(visibility, Is.Not.Null);
			Assert.That(visibility!.ParameterName, Is.EqualTo(dependsOn));
			Assert.That(visibility.Values, Is.EqualTo(values.Split(',')));
		});
	}

	[TestCase("method")]
	[TestCase("url")]
	[TestCase("query")]
	[TestCase("headers")]
	[TestCase("authType")]
	[TestCase("bodyType")]
	[TestCase("expectedStatus")]
	[TestCase("captures")]
	public void Always_applicable_parameters_carry_no_condition(string parameterName)
	{
		Assert.That(SendRequestParameter(parameterName).VisibleWhen, Is.Null);
	}

	private ActionParameter SendRequestParameter(string name)
		=> _integration.Actions
			.Single(action => action.Id == "send-request")
			.Parameters
			.Single(parameter => parameter.Name == name);
}
