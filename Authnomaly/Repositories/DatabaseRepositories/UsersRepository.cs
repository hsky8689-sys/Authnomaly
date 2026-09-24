using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
            return addedLines.Equals(1) ? entity.Id : 0;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.Entry(entity).State = EntityState.Detached;
            return 0;
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
            return  (await _context.users.AsNoTracking()
                .Where(u => u.Id.Equals(id))
                .FirstOrDefaultAsync()) ?? new User(0,"","");
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
            return await _context.users.Where(u=>u.Id.Equals(id))
                                       .ExecuteDeleteAsync() == 1;
        }
        catch
        {
            throw;
        }
    }
    public async Task<User> FindByUsername(string username)
    {
        try
        {
            return (await _context.users.AsNoTracking()
                .Where(u => u.Username.Equals(username))
                .FirstOrDefaultAsync()) ?? new User(0,"","");
        }
        catch
        {
            throw;
        }
    }
}