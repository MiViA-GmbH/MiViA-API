using System.Text.Json.Serialization;

namespace MiviaDesktop.Entities;

public class ModelSettings
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class ModelCustomization
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

/// <summary>
/// API response shape for GET /models/{id}/customizations
/// </summary>
public class ModelCustomizationResponse
{
    public string Id { get; set; } = null!;

    [JsonPropertyName("name")]
    public ModelCustomizationName Name { get; set; } = null!;
}

public class ModelCustomizationName
{
    public string En { get; set; } = null!;
    public string De { get; set; } = null!;
}