using System.Data;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class UsersRepository : IUsersRepo
{
    private readonly AuthnomalyDatabaseContext _context;
    public UsersRepository(AuthnomalyDatabaseContext context)
    {
        _context = context;
    }
    public async Task<long> Add(User entity)
    {
        try
        {
            await _context.users.AddAsync(entity);
            var addedLines = await _context.SaveChangesAsync();
            return addedLines.Equals(1) ? entity.Id : -1;
        }
        catch
        {
            throw;
        }
    }
    public async Task<User> FindById(long id)
    {
        try
        {
            User? found = await _context.users.AsNoTracking()
                .Where(u => u.Id.Equals(id))
                .FirstOrDefaultAsync();
            return found;
        }
        catch
        {
            throw;
        } 
    }
    public async Task<bool> Delete(long id)
    {
        try
        { 
            await _context.users.Where(u=>u.Id.Equals(id))
                                .ExecuteDeleteAsync();
            var deleted = await _context.SaveChangesAsync();
            return deleted == 1;
        }
        catch
        {
            throw;
        }
    }

    public async Task<User> Login(string username, string password)
    {
        try
        {
            User? found = await _context.users.AsNoTracking()
                .Where(u => u.Username.Equals(username))
                .FirstOrDefaultAsync();
            if (found is null)
            {
                return null;
            }
            var hashedData = Encryption.HashPassword(password);
            return Encryption.VerifyPassword(password, hashedData.Hash, hashedData.Salt)
                ? found
                : null;
        }
        catch
        {
            throw;
        }
    }
}