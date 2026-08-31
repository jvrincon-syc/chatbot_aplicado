using System.ComponentModel.DataAnnotations;

namespace Chatbot.Sst.Infrastructure.Redis;

/// <summary>
/// Redis connection + chat event-stream tuning. Password is kept separate from
/// <see cref="Configuration"/> so it can come from an env var / secret and never be committed.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string Configuration { get; init; } = "localhost:6379";

    public string? Password { get; init; }

    /// <summary>Approximate cap on a request's event stream (deltas + terminal). Bounds memory.</summary>
    public int EventStreamMaxLength { get; init; } = 4000;

    /// <summary>TTL for a request's event stream key. Transient result state, not an archive.</summary>
    public int EventTtlHours { get; init; } = 24;
}
