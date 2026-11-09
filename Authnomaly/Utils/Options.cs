using Microsoft.Extensions.Primitives;

namespace Authnomaly.Utils;
//options classes,implementing IConfiguration to substitute
//enviroment variables coupled tightly to authnomaly module
//not re-usable for unit testing or further new modules
public class JwtOptions : IConfiguration
{
    public IEnumerable<IConfigurationSection> GetChildren()
    {
        throw new NotImplementedException();
    }
    public IChangeToken GetReloadToken()
    {
        throw new NotImplementedException();
    }
    public IConfigurationSection GetSection(string key)
    {
        throw new NotImplementedException();
    }
    public string? this[string key]
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
}