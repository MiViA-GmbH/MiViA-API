using System;
using System.Text.Json.Serialization;

namespace MiviaDesktop.Entities;

public enum ReportStatus
{
    PENDING,
    DONE,
    FAILED,
    CANCELLED,
}

public class RemoteReport
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("status")] public ReportStatus Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("pages")] public int? Pages { get; set; }
    [JsonPropertyName("bytes")] public long? Bytes { get; set; }
}

/// <summary>
/// Shape of POST /api/reports/pdf — the id lives under "reportId", not "id".
/// </summary>
public class CreatedReport
{
    [JsonPropertyName("reportId")] public Guid ReportId { get; set; }
    [JsonPropertyName("status")] public ReportStatus Status { get; set; }
}
