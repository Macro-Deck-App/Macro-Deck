using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MacroDeckHost.Infrastructure.Persistence.Interceptors;

public class SaveChangesSetTimestampsInterceptor : ISaveChangesInterceptor
{
	public ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		var context = eventData.Context;
		if (context?.ChangeTracker.Entries() is null)
		{
			return ValueTask.FromResult(result);
		}

		foreach (var entry in context.ChangeTracker.Entries())
		{
			if (entry is { Entity: BaseEntity baseCreatedEntity, State: EntityState.Added })
			{
				if (baseCreatedEntity.Id == Guid.Empty)
				{
					baseCreatedEntity.Id = Guid.CreateVersion7();
				}

				baseCreatedEntity.CreatedAt = DateTime.Now;
			}
		}

		return ValueTask.FromResult(result);
	}
}
