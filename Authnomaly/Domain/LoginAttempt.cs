using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Domain;
[Table("LoginAttempts")]
[PrimaryKey("Id")]
[Index("AttemptTime")]
public class LoginAttempt : Entity<long>
{
    private long _applicationId = default!;
    private string? _username = default!;
    private DateTimeOffset? _attemptTime = default!;
    private string? _deviceName = default!;
    private IPAddress? _ipAddress = default!;
    private string? _browser = default!;
    private int? _port = default!;
    private string? _operatingSystem = default!;
    private bool? _succeeded = default!;
    private string? _country = default!;
    private string? _city = default!;
    [MaxLength(128)]
    [NotNull]
    [MinLength(4)]
    public string? Username
    {
        get => _username;
        set => _username = value ?? "N/A";
    }
    [DefaultValue(1)]
    public long? ApplicationId
    {
        get => _applicationId;
        set => _applicationId = value ?? -1;
    }
    [DefaultValue("N/A")]
    [MaxLength(128)]
    public string? DeviceName
    {
        get => _deviceName;
        set => _deviceName = value ?? "N/A";
    }
    public DateTimeOffset? AttemptTime
    {
        get => _attemptTime;
        set => _attemptTime = value ?? DateTimeOffset.Now;
    }
    [DefaultValue("N/A")]
    [MaxLength(128)]
    public string? Country
    {
        get => _country;
        set => _country = value ?? "N/A";
    }
    [DefaultValue("N/A")]
    [MaxLength(128)]
    public string? City
    {
        get => _city;
        set => _city = value ?? "N/A";
    }
    [DefaultValue("N/A")]
    [MaxLength(128)]
    public string? Browser
    {
        get => _browser;
        set => _browser = value ?? "N/A";
    }
    [DefaultValue("N/A")]
    [MaxLength(128)]
    public string? OperatingSystem
    {
        get => _operatingSystem;
        set => _operatingSystem = value ?? "N/A";
    }
    [DefaultValue(false)]
    public bool? Succeeded
    {
        get => _succeeded;
        set => _succeeded = value ?? false;
    }
    [DefaultValue("127.0.0.1")]
    public IPAddress? IpAddress
    {
        get => _ipAddress;
        set => _ipAddress = value ?? System.Net.IPAddress.Parse("127.0.0.1");
    }

    [DefaultValue(1)]
    public int? Port
    {
        get => _port;
        set => _port = value;
    }

    public LoginAttempt(long id) : base(id)
    {
        
    }
}