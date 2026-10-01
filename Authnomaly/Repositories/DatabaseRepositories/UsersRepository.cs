using System.Data;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class UsersRepository : IUsersRepo
{
    private readonly ILogger _logger;
    private readonly AuthnomalyDatabaseContext _context;

    public UsersRepository(AuthnomalyDatabaseContext context,ILogger logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<long> Add(User entity)
    {
        try
        {
            return await _context.Database
                .SqlQuery<long>($@"SELECT add_user({entity.Username},{entity.Email}) AS ""Value""")
                .SingleOrDefaultAsync();
            await _context.users.AddAsync(entity);
            var addedLines = await _context.SaveChangesAsync();
            return addedLines == 1 ? entity.Id : 0;
        }
        catch (PostgresException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.Entry(entity).State = EntityState.Detached;
            _logger.LogError($"{e.InnerException.Message} from users repository");
            return 0;
        }
        catch (SystemException e) when (e.InnerException is ArgumentNullException | 
                                  e.InnerException is InvalidOperationException |
                                  e.InnerException is OperationCanceledException)
        {
            _logger.LogError($"{e.InnerException?.Message} from users repository");
            //_context.Entry(entity).State = EntityState.Detached; 
            return 0;
        }
        catch (Exception e)
        {
            throw;
        }
    }

    public async Task<User> FindById(long id)
    {
        try
        {
            return (await _context.users.AsNoTracking()
                .Where(u => u.Id.Equals(id))
                .SingleOrDefaultAsync()) ?? new User(0, "", "");
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
            return await _context.users
                                 .Where(u => u.Id.Equals(id))
                                 .ExecuteDeleteAsync() == 1;
        }
        catch
        {
            throw;
        }
    }

    public async Task<User> FindByUsername(string username)
    {
            return (await _context.users
                .Where(u => u.Username.Equals(username))
                .SingleOrDefaultAsync()) ?? new User(0, "", "");
    }

    public async Task<bool> ChangeUsername(long userId, string oldUsername,string newUsername)
    {
        try
        {
            return await _context.users.Where(u => u.Id == userId && u.Username.Equals(oldUsername))
                       .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Username, newUsername)) == 1;
        }
        catch (PostgresException pe)
        {
            return false;
        }
    }
}