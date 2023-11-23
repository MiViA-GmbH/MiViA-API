using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiviaDesktop;

public static class Serialization
{
    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = {
                new JsonStringEnumConverter()
            }
        })!;
    }
}