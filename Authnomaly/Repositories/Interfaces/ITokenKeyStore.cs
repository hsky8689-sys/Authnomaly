namespace Authnomaly.Repositories.Interfaces;
public enum BlacklistLevel
{
    NotBlacklisted = 0,
    Expired = 1,
    AdminEnforced = 2,
    AnomalyDetected = 3,
    Logout = 4
}
public interface ITokenBlacklistStore
{
    Task<bool> AddToBlacklist(string jti,int ttl,BlacklistLevel reason);
    Task<BlacklistLevel> IsBlacklisted(string jti);
    Task<string?> GetCurrentFamilyJti(Guid familyId);
    Task SetCurrentFamilyJti(Guid familyId, string jti, TimeSpan ttl);
    Task RevokeFamily(Guid familyId);
    Task<bool> IsFamilyRevoked(Guid familyId);
}