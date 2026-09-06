using System.Globalization;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using DomainScope = MacroDeckHost.Domain.Enums.VariableScope;
using DomainType = MacroDeckHost.Domain.Enums.VariableType;
using DomainClassification = MacroDeckHost.Domain.Enums.VariableClassification;

namespace MacroDeckHost.Application.Ui.Handlers;

internal static class VariableDtoMapper
{
	public static Variable ToDto(VariableEntity entity, bool available, string? dynamicResourceId)
	{
		return new Variable
		{
			Id = entity.Id.ToString(),
			Name = entity.Name,
			DisplayName = entity.Presentation?.DisplayName ?? default,
			ConfigurationKey = entity.Presentation?.ConfigurationKey,
			ConfigurationName = entity.Presentation?.ConfigurationName ?? default,
			Scope = ScopeToWire(entity.Scope),
			ScopeRefId = entity.ScopeRefId,
			Type = TypeToWire(entity.Type),
			Classification = ClassificationToWire(entity.Classification),
			OwnerIntegrationId = entity.OwnerIntegrationId,
			Value = entity.Value,
			DecimalPlaces = entity.DecimalPlaces,
			Unit = entity.Unit,
			SemanticKind = entity.SemanticKind,
			Attributes = entity.Attributes,
			Min = entity.Min,
			Max = entity.Max,
			Step = entity.Step,
			CanWrite = entity.CanWrite,
			CommitOnRelease = entity.CommitOnRelease,
			Available = available,
			DynamicResourceId = dynamicResourceId,
		};
	}

	public static string ScopeToWire(DomainScope s) => s switch
	{
		DomainScope.Global => "global",
		DomainScope.Widget => "widget",
		_ => s.ToString().ToLowerInvariant()
	};

	public static DomainScope? ScopeFromWire(string? wire)
	{
		if (string.IsNullOrEmpty(wire))
		{
			return null;
		}

		return wire.ToLowerInvariant() switch
		{
			"global" => DomainScope.Global,
			"widget" => DomainScope.Widget,
			// The scope was called "actionButton" before it was generalized to every widget type; a client
			// built against the old vocabulary still sends it.
			"actionbutton" => DomainScope.Widget,
			_ => null
		};
	}

	public static string TypeToWire(DomainType t) => t switch
	{
		DomainType.Text => "text",
		DomainType.Numeric => "numeric",
		DomainType.Boolean => "boolean",
		_ => t.ToString().ToLowerInvariant()
	};

	public static DomainType? TypeFromWire(string? wire)
	{
		if (string.IsNullOrEmpty(wire))
		{
			return null;
		}

		return wire.ToLowerInvariant() switch
		{
			"text" => DomainType.Text,
			"numeric" => DomainType.Numeric,
			"boolean" => DomainType.Boolean,
			_ => null
		};
	}

	public static string ClassificationToWire(DomainClassification c) => c switch
	{
		DomainClassification.User => "user",
		DomainClassification.Integration => "integration",
		DomainClassification.Widget => "widget",
		_ => c.ToString().ToLowerInvariant()
	};

	public static object? ParseInputValue(DomainType type, string? raw)
	{
		return type switch
		{
			DomainType.Text => raw ?? string.Empty,
			DomainType.Numeric => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
				? d
				: 0m,
			DomainType.Boolean => string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase),
			_ => raw
		};
	}

	public static Transport.Messages.TransportError ToError(VariableError error, string? message)
	{
		return new Transport.Messages.TransportError
		{
			Code = error.ToString(),
			Message = message ?? string.Empty
		};
	}

	/// <summary>
	/// The refusals a write can produce, with a message a person can read. Every other error keeps the
	/// service's plain diagnostic text, which is what <see cref="ToError"/> has always sent.
	/// </summary>
	public static Transport.Messages.TransportError ToWriteError(
		VariableError error,
		string? message,
		string? ownerIntegrationId)
		=> error switch
		{
			VariableError.NotWritable => new Transport.Messages.TransportError
				{ Code = error.ToString(), Message = AppStrings.Errors.Variables.NotWritable() },
			VariableError.OwnerUnavailable => new Transport.Messages.TransportError
			{
				Code = error.ToString(),
				Message = AppStrings.Errors.Variables.OwnerUnavailable(integration: ownerIntegrationId ?? string.Empty)
			},
			VariableError.WriteFailed => new Transport.Messages.TransportError
				{ Code = error.ToString(), Message = AppStrings.Errors.Variables.WriteFailed() },
			_ => ToError(error, message)
		};
}
