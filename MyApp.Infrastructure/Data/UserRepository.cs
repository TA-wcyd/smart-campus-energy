using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

/// <summary>
/// Repository for User persistence.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        return await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    /// <inheritdoc/>
    public async Task<User> UpsertAsync(User user, CancellationToken ct)
    {
        var existing = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == user.Email, ct);

        if (existing is null)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
            return user;
        }

        existing.DisplayName = user.DisplayName;
        await _db.SaveChangesAsync(ct);
        return existing;
    }
}
