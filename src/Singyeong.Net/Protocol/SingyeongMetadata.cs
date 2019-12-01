using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal class SingyeongMetadata
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("value")]
        public object? Value { get; set; }
    }
}
