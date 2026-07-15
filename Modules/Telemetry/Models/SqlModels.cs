using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Telemetry.Models;

public class LogModelSql
{
    [Key]
    public long Id { get; set; }

    public DateTime Time { get; set; }

    [MaxLength(2048)]
    public string Uri { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Uid { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? UnfoId { get; set; }

    public int DurationMs { get; set; }

    [MaxLength(128)]
    public string? Balancer { get; set; }

    public int StatusCode { get; set; }

    [MaxLength(256)]
    public string? MovieTitle { get; set; }

    [MaxLength(32)]
    public string? TmdbId { get; set; }

    [MaxLength(32)]
    public string? KpId { get; set; }

    [MaxLength(32)]
    public string? ImdbId { get; set; }

    public bool IsTv { get; set; }
}

public class UserInfoModelSql
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Ip { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string UserAgent { get; set; } = string.Empty;
}
