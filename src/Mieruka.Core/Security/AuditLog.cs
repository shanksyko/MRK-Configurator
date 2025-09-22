using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mieruka.Core.Security;

/// <summary>
/// Provides structured security audit logging with rotation and retention.
/// </summary>
public sealed class AuditLog
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private readonly long _maxLogSizeBytes;
    private readonly int _retentionCount;

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLog"/> class.
    /// </summary>
    /// <param name="directory">Directory that should host the audit log.</param>
    /// <param name="maxLogSizeBytes">Maximum size before a rotation occurs.</param>
    /// <param name="retentionCount">Number of retained rotated files.</param>
    public AuditLog(string? directory = null, long maxLogSizeBytes = 1_048_576, int retentionCount = 5)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = directory ?? Path.Combine(localAppData, "Mieruka");
        Directory.CreateDirectory(baseDirectory);

        _logPath = Path.Combine(baseDirectory, "audit.jsonl");
        _maxLogSizeBytes = Math.Max(64 * 1024, maxLogSizeBytes);
        _retentionCount = Math.Max(1, retentionCount);
    }

    /// <summary>
    /// Records a login attempt event.
    /// </summary>
    public void RecordLoginAttempt(string siteId, bool success)
    {
        WriteEvent(new AuditEvent("login_attempt")
        {
            SiteId = siteId,
            Result = success ? "success" : "failure",
        });
    }

    /// <summary>
    /// Records that cookies have been stored.
    /// </summary>
    public void RecordCookieStored(string host, int count)
    {
        WriteEvent(new AuditEvent("cookie_store")
        {
            Host = host,
            Count = count,
        });
    }

    /// <summary>
    /// Records that cookies have been restored.
    /// </summary>
    public void RecordCookieRestored(string host, int count)
    {
        WriteEvent(new AuditEvent("cookie_restore")
        {
            Host = host,
            Count = count,
        });
    }

    /// <summary>
    /// Records that cookies have been revoked.
    /// </summary>
    public void RecordCookieRevoked(string host)
    {
        WriteEvent(new AuditEvent("cookie_revoke")
        {
            Host = host,
        });
    }

    /// <summary>
    /// Records that cookies were blocked due to policy restrictions.
    /// </summary>
    public void RecordCookieBlocked(string host)
    {
        WriteEvent(new AuditEvent("cookie_blocked")
        {
            Host = host,
        });
    }

    /// <summary>
    /// Records that navigation was blocked by the allowlist.
    /// </summary>
    public void RecordAllowlistBlock(string url, string? siteId)
    {
        WriteEvent(new AuditEvent("allowlist_block")
        {
            SiteId = siteId,
            Url = url,
        });
    }

    /// <summary>
    /// Records an integrity validation failure.
    /// </summary>
    public void RecordIntegrityFailure(string path, string expectedHash, string actualHash)
    {
        WriteEvent(new AuditEvent("integrity_failed")
        {
            Path = path,
            ExpectedHash = expectedHash,
            ActualHash = actualHash,
        });
    }

    /// <summary>
    /// Records that a policy override was applied.
    /// </summary>
    public void RecordPolicyOverride(string siteId, string setting)
    {
        WriteEvent(new AuditEvent("policy_override")
        {
            SiteId = siteId,
            Setting = setting,
        });
    }

    /// <summary>
    /// Writes a structured event to the audit log.
    /// </summary>
    /// <param name="payload">Event payload.</param>
    public void WriteEvent(AuditEvent payload)
    {
        lock (_sync)
        {
            RotateIfNeeded();
            payload.Timestamp = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(payload, _serializerOptions);
            File.AppendAllText(_logPath, json + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void RotateIfNeeded()
    {
        var fileInfo = new FileInfo(_logPath);
        if (fileInfo.Exists && fileInfo.Length >= _maxLogSizeBytes)
        {
            for (var index = _retentionCount - 1; index >= 0; index--)
            {
                var rotated = index == 0 ? _logPath : $"{_logPath}.{index}";
                if (File.Exists(rotated))
                {
                    if (index + 1 >= _retentionCount)
                    {
                        File.Delete(rotated);
                    }
                    else
                    {
                        var next = $"{_logPath}.{index + 1}";
                        File.Move(rotated, next, overwrite: true);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Represents an audit log entry.
    /// </summary>
    public sealed class AuditEvent
    {
        public AuditEvent(string type)
        {
            Type = type;
        }

        [JsonPropertyName("@timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; }

        [JsonPropertyName("site_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SiteId { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Result { get; set; }

        [JsonPropertyName("host")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Host { get; set; }

        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Count { get; set; }

        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Url { get; set; }

        [JsonPropertyName("path")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        [JsonPropertyName("expected_hash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExpectedHash { get; set; }

        [JsonPropertyName("actual_hash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ActualHash { get; set; }

        [JsonPropertyName("setting")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Setting { get; set; }
    }
}
