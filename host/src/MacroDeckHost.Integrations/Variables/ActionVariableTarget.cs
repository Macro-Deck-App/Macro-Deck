using System.Globalization;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Variables;

internal static class ActionVariableTarget
{
	public static async Task<bool> WriteAsync(
		IUserVariableApi? userVariables,
		IVariableApi? integrationVariables,
		string name,
		string? ownerWidgetId,
		VariableType type,
		object value,
		ILogger logger,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Variables this integration created for a target before targets became user variables keep
			// updating, so widgets bound to them do not go stale.
			if (integrationVariables is not null &&
				await integrationVariables.GetByNameAsync(name).ConfigureAwait(false) is { } owned)
			{
				await integrationVariables.SetValueAsync(owned.Id, value).ConfigureAwait(false);
				return true;
			}

			if (userVariables is null)
			{
				logger.Warning("Cannot write variable '{Variable}': variable API unavailable", name);
				return false;
			}

			var text = Format(value);
			var applied = await userVariables
				.ApplyAsync(name, ownerWidgetId, UserVariableOperation.Set, text, cancellationToken)
				.ConfigureAwait(false);
			if (applied.Status == UserVariableWriteStatus.NotFound)
			{
				var created = await userVariables
					.CreateAsync(name, null, type, text, cancellationToken: cancellationToken)
					.ConfigureAwait(false);
				if (created.Status == UserVariableCreateStatus.Created)
				{
					return true;
				}

				if (created.Status != UserVariableCreateStatus.AlreadyExists)
				{
					logger.Warning("Could not create variable '{Variable}': {Status} {Message}",
						name,
						created.Status,
						created.Message);
					return false;
				}

				applied = await userVariables
					.ApplyAsync(name, ownerWidgetId, UserVariableOperation.Set, text, cancellationToken)
					.ConfigureAwait(false);
			}

			if (applied.Status == UserVariableWriteStatus.Applied)
			{
				return true;
			}

			logger.Warning("Could not write variable '{Variable}': {Status} {Message}",
				name,
				applied.Status,
				applied.Message);
			return false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.Warning(ex, "Could not write variable '{Variable}'", name);
			return false;
		}
	}

	// UserVariableWriter parses numbers without exponents, so doubles go through decimal first.
	private static string Format(object value) => value switch
	{
		bool flag => flag ? "true" : "false",
		string text => text,
		double or float or decimal or int or long or short or byte or uint or ulong or ushort or sbyte
			=> Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty
	};
}
