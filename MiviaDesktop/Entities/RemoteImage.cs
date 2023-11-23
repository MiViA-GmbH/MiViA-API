using System;
using System.Text.Json.Serialization;

namespace MiviaDesktop.Entities;

public partial class RemoteImage
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
}
