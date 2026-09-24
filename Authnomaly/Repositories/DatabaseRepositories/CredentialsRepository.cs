using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;
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
            var res= await _context.authCredentials
                                                         .AsNoTracking()
                                                         .Where(c => c.Owner.Id.Equals(userId))
                                                         .FirstOrDefaultAsync();
            return res is null ? new AuthCredentials(0) : res;
        }
        catch
        {
            throw;
        }
    }
    public async Task<bool> ChangePassword(long userId, string newPassword)
    {
        try
        {
            AuthCredentials found = await FindById(userId);
            if (!found.Id.Equals(0))
            {
                var hashResults = Encryption.HashPassword(newPassword);
                found.PasswordHash = Convert.ToBase64String(hashResults.Hash);
                found.Salt = hashResults.Salt;
                var changed = await _context.SaveChangesAsync();
                return changed == 1;
            }
            return false;
        }
        catch
        {
            throw;
        }
    }
    public async Task<bool> ChangeUsername(long userId, string newUsername)
    {
        try
        {
            AuthCredentials found = await FindById(userId);
            if (!found.Id.Equals(0))
            {
                found.Username = newUsername;
                var changed = await _context.SaveChangesAsync();
                return changed == 1;
            }
            return false;
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
            await _context.AddAsync(entity);
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