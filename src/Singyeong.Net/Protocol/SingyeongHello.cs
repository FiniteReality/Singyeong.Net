using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongHello
    {
        [JsonPropertyName("heartbeat_interval")]
        public int HeartbeatIntervalMs { get; set; }
    }
}