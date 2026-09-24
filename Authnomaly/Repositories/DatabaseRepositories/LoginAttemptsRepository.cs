using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class LoginAttemptsRepository : ILoginAttemptsRepo
{
    private readonly AuthnomalyDatabaseContext _context;
    public LoginAttemptsRepository(AuthnomalyDatabaseContext context)
    {
        _context = context;
    }
    public async Task<LoginAttempt> FindById(long id)
    {
        try
        {
            var found = await _context.loginAttempts.
                                   Where(la => la.Id.Equals(id)).
                                   FirstOrDefaultAsync();
            return found is not null ? found : new LoginAttempt(0);
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
            return await _context.loginAttempts.Where(la => la.Id.Equals(id))
                                               .ExecuteDeleteAsync() == 1;
        }
        catch
        {
            throw;
        }
    }
    public async Task<long> Add(LoginAttempt entity)
    {
        try
        {
            await _context.loginAttempts.AddAsync(entity);
            var added = await _context.SaveChangesAsync();
            return added == 1 ? entity.Id : 0;
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
}