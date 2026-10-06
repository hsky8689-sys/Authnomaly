namespace Authnomaly.Middlewares.RateLimiting;

public class RateLimitAttribute : Attribute
{
    private string _name;
    public string Name
    {
        get => _name;
        set => _name = value ?? throw new ArgumentNullException(nameof(value));
    }
    public RateLimitAttribute(string name)
    {
        Name = name;
    }
}