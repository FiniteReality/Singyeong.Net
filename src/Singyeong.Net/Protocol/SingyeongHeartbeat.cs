using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongHeartbeat
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
    }
}