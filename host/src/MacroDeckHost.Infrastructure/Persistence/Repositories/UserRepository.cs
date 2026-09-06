using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MacroDeckHost.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
	private readonly DatabaseContext _context;

	public UserRepository(DatabaseContext context)
	{
		_context = context;
	}

	public Task<UserEntity?> GetSingle() => _context.Users.FirstOrDefaultAsync();

	public Task<bool> AnyExists() => _context.Users.AnyAsync();

	public async Task Create(UserEntity user)
	{
		await _context.Users.AddAsync(user);
		await _context.SaveChangesAsync();
	}

	public async Task Update(UserEntity user)
	{
		_context.Users.Update(user);
		await _context.SaveChangesAsync();
	}
}
