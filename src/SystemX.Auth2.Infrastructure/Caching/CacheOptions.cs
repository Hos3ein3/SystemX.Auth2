namespace SystemX.Auth2.Infrastructure.Caching;

public enum CacheProvider { Memory, Redis }

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public CacheProvider Provider { get; set; } = CacheProvider.Memory;
    public string? RedisConnection { get; set; }
    public string InstanceName { get; set; } = "auth2:";
    public int PermissionTtlSeconds { get; set; } = 300;
}