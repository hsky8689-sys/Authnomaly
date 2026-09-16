using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

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
            return res is null ? new AuthCredentials(-1) : res;
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
            if (!found.Id.Equals(-1))
            {
                found.Password = newPassword;
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
            if (!found.Id.Equals(-1))
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
            return res is null ? new AuthCredentials(-1) : res;
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
            var deleted = await _context.authCredentials.Where(c => c.Id.Equals(id)).ExecuteDeleteAsync();
            return deleted == 1;
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
            return added == 1 ? entity.Id : -1;
        }
        catch
        {
            throw;
        }
    }
}