using System;
using System.Text.Json.Serialization;

namespace MiviaDesktop.Entities;

public partial class RemoteJob
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("resultId")] public Guid? ResultId { get; set; }
    [JsonPropertyName("image")] public Image Image { get; set; } = null!;
    [JsonPropertyName("model")] public Model Model { get; set; } = null!;
    [JsonPropertyName("status")] public JobStatus Status { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public enum JobStatus
{
    CACHED,
    NEW,
    FAILED,
}

public partial class Image
{
    [JsonPropertyName("id")] public Guid Id { get; set; }

    [JsonPropertyName("orginalFilename")] public string OrginalFilename { get; set; } = null!;
}

public partial class Model
{
    [JsonPropertyName("id")] public Guid Id { get; set; }

    [JsonPropertyName("internalName")] public string InternalName { get; set; } = null!;

    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = null!;
}