using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableBindingAppender
{
	private readonly IVariableBindingStore _store;
	private readonly TimeProvider _time;

	public VariableBindingAppender(IVariableBindingStore store, TimeProvider? time = null)
	{
		_store = store;
		_time = time ?? TimeProvider.System;
	}

	public async Task<IReadOnlyList<VariableBindingOutcome>> AppendAsync(
		string integrationId,
		IReadOnlyCollection<VariableBindingRequest> requests,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(integrationId);
		ArgumentNullException.ThrowIfNull(requests);
		foreach (var request in requests)
		{
			Validate(request);
		}

		if (requests.Count == 0)
		{
			return [];
		}

		await VariableBindingService.MutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_store.TryLoad(out var existing))
			{
				throw new InvalidOperationException("The variable bindings could not be read.");
			}

			var known = existing
				.Where(binding => string.Equals(binding.IntegrationId, integrationId, StringComparison.Ordinal))
				.Select(binding => binding.LocalResourceId)
				.ToHashSet(StringComparer.Ordinal);

			var now = _time.GetUtcNow().UtcDateTime;
			var added = new List<VariableBinding>();
			var outcomes = new List<VariableBindingOutcome>(requests.Count);
			foreach (var request in requests)
			{
				if (!known.Add(request.LocalResourceId))
				{
					outcomes.Add(VariableBindingOutcome.AlreadyBound);
					continue;
				}

				added.Add(new VariableBinding
				{
					Id = Guid.CreateVersion7(),
					IntegrationId = integrationId,
					LocalResourceId = request.LocalResourceId,
					Name = request.Name,
					Type = SdkVariableTypeMapper.ToDomain(request.Type),
					DecimalPlaces = request.DecimalPlaces,
					CreatedAt = now,
					MigratedFrom = string.IsNullOrEmpty(request.MigratedFrom) ? null : request.MigratedFrom
				});
				outcomes.Add(VariableBindingOutcome.Bound);
			}

			if (added.Count > 0 && !_store.Save(existing.Concat(added).ToList()))
			{
				throw new InvalidOperationException("The variable bindings could not be saved.");
			}

			return outcomes;
		}
		finally
		{
			VariableBindingService.MutationLock.Release();
		}
	}

	private static void Validate(VariableBindingRequest? request)
	{
		if (request is null)
		{
			throw new ArgumentException("A binding request is missing.");
		}

		if (!MacroDeckId.IsValidLocalId(request.LocalResourceId, LocalIdKind.Resource))
		{
			throw new ArgumentException($"'{request.LocalResourceId}' is not a valid resource id.");
		}

		if (!VariableNames.IsValid(request.Name))
		{
			throw new ArgumentException($"'{request.Name}' is not a canonical variable name.");
		}

		if (!Enum.IsDefined(request.Type))
		{
			throw new ArgumentException($"'{request.Type}' is not a variable type.");
		}
	}
}
