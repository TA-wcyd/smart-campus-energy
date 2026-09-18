using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
    }

    public async Task<User> UpsertAsync(User u, CancellationToken ct = default)
    {
        var existing = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == u.Email.ToLower(), ct);
        if (existing == null)
        {
            _dbContext.Users.Add(u);
            await _dbContext.SaveChangesAsync(ct);
            return u;
        }

        existing.DisplayName = u.DisplayName;
        await _dbContext.SaveChangesAsync(ct);
        return existing;
    }
}
