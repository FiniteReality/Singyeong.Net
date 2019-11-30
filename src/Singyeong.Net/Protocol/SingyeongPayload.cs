using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal class SingyeongPayload
    {
        [JsonPropertyName("op")]
        public SingyeongOpcode Opcode { get; set; }

        [JsonPropertyName("d")]
        public object? Payload { get; set; }
    }
}