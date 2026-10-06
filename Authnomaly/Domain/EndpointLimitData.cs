using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Domain;
public enum RateLimitStrategy
{
    FixedWindow,
    SlidingWindow,
    Bucket
}
public enum RateLimitSource
{
    Ip,BodyField
}
[Table("EndpointsRateLimitStats")]
[PrimaryKey("Id")]
public class EndpointLimitData : Entity<long>
{
    private int _order,_windowSeconds,_limit;
    private RateLimitStrategy _strategy = default!;
    private string _fieldName = default!;
    private HttpMethod _method = default!;
    private string _path = default!;
    public RateLimitSource Source;
    public string Path
    {
        get => _path;
        set => _path = value ?? throw new ArgumentNullException(nameof(value));
    }
    public HttpMethod Method
    {
        get => _method;
        set => _method = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string FieldName
    {
        get => _fieldName;
        set => _fieldName = Source.Equals(RateLimitSource.BodyField) ? (value ?? ""): "";
    }
    public int Order
    {
        get => _order;
        set => _order = value >= 1 ? value : 1;
    }
    public int WindowSeconds
    {
        get => _windowSeconds;
        set => _windowSeconds = value;
    }
    public int Limit
    {
        get => _limit;
        set => _limit = value;
    }
    public RateLimitStrategy Strategy
    {
        get => _strategy;
        set => _strategy = value;
    }
    public EndpointLimitData(long id) : base(id)
    {
        
    }
    public EndpointLimitData(long id,
                              HttpMethod method,
                              string path,
                              int order,
                              RateLimitSource source,
                              int windowSeconds,
                              int limit,
                              string? fieldName,
                              RateLimitStrategy strategy) : base(id)
    {
        Method = method;
        Path = path;
        Order = order;
        Source = source;
        WindowSeconds = windowSeconds;
        Limit = limit;
        FieldName = fieldName ?? "";
        Strategy = strategy;
    }
}