using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongReady
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
    }
}