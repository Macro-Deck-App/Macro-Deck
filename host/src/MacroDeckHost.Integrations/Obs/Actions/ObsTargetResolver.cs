using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class ObsTargetResolver
{
	public const string ConfigurationParameter = "configuration";

	private readonly Func<IReadOnlyList<ObsRuntime>> _runtimes;
	private readonly Func<ObsConnection?>? _legacyResolver;

	public ObsTargetResolver(Func<IReadOnlyList<ObsRuntime>> runtimes)
	{
		_runtimes = runtimes;
	}

	private ObsTargetResolver(Func<ObsConnection?> legacyResolver)
	{
		_runtimes = () => [];
		_legacyResolver = legacyResolver;
	}

	public static ObsTargetResolver Legacy(Func<ObsConnection?> resolver) => new(resolver);

	public static ActionParameter Parameter()
		=> ActionParameter.DynamicChoice(ConfigurationParameter,
			label: AppStrings.Integrations.Obs.Params.Configuration(),
			required: true);

	public DynamicOptionsResult ConfigurationOptions()
		=> new()
		{
			Options = _runtimes()
				.Select(runtime => new ActionParameterOption
				{
					Value = runtime.Id.ToString("D"),
					Label = runtime.Title
				})
				.ToList(),
			CacheSeconds = 0
		};

	public ObsConnection? ForOptions(IReadOnlyDictionary<string, object?> parameters)
	{
		if (_legacyResolver is not null)
		{
			return _legacyResolver();
		}

		return TryFind(parameters, out var runtime) && runtime.Connection.IsConnected
			? runtime.Connection
			: null;
	}

	public bool TryResolve(IReadOnlyDictionary<string, object> parameters,
		out ObsConnection connection,
		out ActionResult error)
	{
		if (_legacyResolver is not null)
		{
			connection = _legacyResolver()!;
			if (connection is not null && connection.IsConnected)
			{
				error = null!;
				return true;
			}

			error = NotConnected();
			return false;
		}

		if (!parameters.TryGetValue(ConfigurationParameter, out var raw) ||
			raw is not string text ||
			!Guid.TryParseExact(text, "D", out var id))
		{
			connection = null!;
			error = ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				AppStrings.Integrations.Obs.Errors.InvalidConfiguration());
			return false;
		}

		var runtime = _runtimes().FirstOrDefault(item => item.Id == id);
		if (runtime is null)
		{
			connection = null!;
			error = ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Obs.Errors.ConfigurationNotFound());
			return false;
		}

		connection = runtime.Connection;
		if (!connection.IsConnected)
		{
			error = NotConnected();
			return false;
		}

		error = null!;
		return true;
	}

	private bool TryFind(IReadOnlyDictionary<string, object?> parameters, out ObsRuntime runtime)
	{
		runtime = null!;
		return parameters.GetValueOrDefault(ConfigurationParameter) is string text &&
			Guid.TryParseExact(text, "D", out var id) &&
			(runtime = _runtimes().FirstOrDefault(item => item.Id == id)!) is not null;
	}

	private static ActionResult NotConnected()
		=> ActionResult.Failed(ActionErrorCodes.NotConnected, AppStrings.Integrations.Obs.Errors.NotConnected());
}
