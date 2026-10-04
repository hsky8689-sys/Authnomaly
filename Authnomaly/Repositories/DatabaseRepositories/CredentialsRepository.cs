using System.Text;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Authnomaly.Repositories.DatabaseRepositories;

public class CredentialsRepository : ICredentialsRepo
{
    private readonly AuthnomalyDatabaseContext _context;
    public CredentialsRepository(AuthnomalyDatabaseContext context)
    {
        _context = context;
    }
    public async Task<AuthCredentials> FindByUserId(long userId)
    {
        try
        {
            var res = await _context.authCredentials
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Owner.Id.Equals(userId));
            return res is null ? new AuthCredentials(0) : res;
        }
        catch
        {
            throw;
        }
    }
    public async Task<bool> ChangePassword(long userId, 
                                           string newPasswordHash,
                                           byte[] newPasswordSalt,
                                           string oldPasswordHash,
                                           byte[] oldPasswordSalt)
    {
        try
        {
            var rows = await _context.authCredentials
                .Where(c => c.Id == userId && c.PasswordHash == oldPasswordHash && c.Salt == oldPasswordSalt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.PasswordHash, newPasswordHash)
                    .SetProperty(c => c.Salt, newPasswordSalt));
            return rows == 1;
        }
        catch
        {
            throw;
        }
    }
    public async Task<AuthCredentials> FindById(long id)
    {
        try
        {
            var res = await _context.authCredentials
                                                                 .Where(c => c.Id.Equals(id))
                                                                 .FirstOrDefaultAsync();
            return res is null ? new AuthCredentials(0) : res;
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
            return await _context.authCredentials.Where(c => c.Id.Equals(id))
                                                 .ExecuteDeleteAsync() == 1;
        }
        catch
        {
            throw;
        }
    }
    public async Task<long> Add(AuthCredentials entity)
    {
        try
        { 
            return await _context.Database
                .SqlQuery<long>($@"SELECT add_user_credentials({entity.Id},{entity.PasswordHash},{entity.Salt}) AS ""Value""")
                .SingleOrDefaultAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.Entry(entity).State = EntityState.Detached;
            return 0;
        } 
        catch(Exception e)
        {
            throw;
        }
    }
}