using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiviaDesktop.Entities;

public partial class RemoteJob
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("imageId")] public Guid ImageId { get; set; }
    [JsonPropertyName("resultId")] public Guid? ResultId { get; set; }
    [JsonPropertyName("finishedAt")] public DateTime? FinishedAt { get; set; }
    [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("status")] public JobStatus Status { get; set; }
    [JsonPropertyName("ownerId")] public Guid OwnerId { get; set; }
    [JsonPropertyName("modelId")] public Guid ModelId { get; set; }
    [JsonPropertyName("modelVersion")] public string ModelVersion { get; set; } = null!;
    [JsonPropertyName("executorId")] public Guid? ExecutorId { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("outdated")] public bool Outdated { get; set; }
    [JsonPropertyName("projectId")] public Guid? ProjectId { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
    [JsonPropertyName("rating")] public int Rating { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = null!;
    [JsonPropertyName("originJobId")] public Guid? OriginJobId { get; set; }
    [JsonPropertyName("withMasks")] public bool WithMasks { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }

    // Nested objects (optional - will be null if not included in JSON)
    [JsonPropertyName("image")] public Image? Image { get; set; }
    [JsonPropertyName("model")] public Model? Model { get; set; }
    [JsonPropertyName("result")] public Result? Result { get; set; }
    [JsonPropertyName("owner")] public Owner? Owner { get; set; }
    [JsonPropertyName("executor")] public Executor? Executor { get; set; }
    [JsonPropertyName("masks")] public List<Mask>? Masks { get; set; }
}

public enum JobStatus
{
    CACHED,
    NEW,
    FAILED,
    PENDING,
}

public partial class Image
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("orginalFilename")] public string OrginalFilename { get; set; } = null!;
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("meta")] public ImageMeta? Meta { get; set; }
    [JsonPropertyName("ownerId")] public Guid? OwnerId { get; set; }
    [JsonPropertyName("hash")] public string? Hash { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("isDeleted")] public bool? IsDeleted { get; set; }
    [JsonPropertyName("validated")] public bool? Validated { get; set; }

    [JsonPropertyName("allowedTrainingUse")]
    public bool? AllowedTrainingUse { get; set; }
}

public partial class ImageMeta
{
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("format")] public string Format { get; set; } = null!;
}

public partial class Model
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("internalName")] public string InternalName { get; set; } = null!;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = null!;
    [JsonPropertyName("routingPath")] public string? RoutingPath { get; set; }
    [JsonPropertyName("storagePath")] public string? StoragePath { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("accessType")] public string? AccessType { get; set; }
    [JsonPropertyName("isDefaultVisible")] public bool? IsDefaultVisible { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public partial class Result
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("sourceImageHash")] public string? SourceImageHash { get; set; }
    [JsonPropertyName("modelId")] public Guid ModelId { get; set; }
    [JsonPropertyName("modelVersion")] public string ModelVersion { get; set; } = null!;
    [JsonPropertyName("obsolete")] public bool Obsolete { get; set; }
    [JsonPropertyName("results")] public List<ResultItem>? Results { get; set; }
    [JsonPropertyName("feedback")] public List<Feedback>? Feedback { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("meta")] public ResultMeta? Meta { get; set; }
    [JsonPropertyName("hasCustomization")] public bool HasCustomization { get; set; }
}

public partial class ResultItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = null!;
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("data")] public List<DataPoint>? Data { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("xLabel")] public string? XLabel { get; set; }
    [JsonPropertyName("yLabel")] public string? YLabel { get; set; }
}

public partial class DataPoint
{
    [JsonPropertyName("x")] public double? X { get; set; }
    [JsonPropertyName("y")] public double? Y { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
}

public partial class Feedback
{
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("value")] public bool Value { get; set; }
}

public partial class ResultMeta
{
    [JsonPropertyName("pix_per_um")] public int PixPerUm { get; set; }
}

public partial class Owner
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("surname")] public string Surname { get; set; } = null!;
}

public partial class Executor
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("gpuAffinity")] public int GpuAffinity { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = null!;
    [JsonPropertyName("nodeId")] public Guid NodeId { get; set; }
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("envVariables")] public string? EnvVariables { get; set; }
    [JsonPropertyName("isUpdating")] public bool IsUpdating { get; set; }
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("node")] public Node? Node { get; set; }
}

public partial class Node
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = null!;
    [JsonPropertyName("ip")] public string Ip { get; set; } = null!;
    [JsonPropertyName("nodeType")] public string NodeType { get; set; } = null!;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = null!;
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
}

public partial class Mask
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("jobId")] public Guid JobId { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = null!;
    [JsonPropertyName("parentId")] public int? ParentId { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; } = null!;
    [JsonPropertyName("editedById")] public Guid? EditedById { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    [JsonPropertyName("isSubmitted")] public bool IsSubmitted { get; set; }
    [JsonPropertyName("isImported")] public bool IsImported { get; set; }
}