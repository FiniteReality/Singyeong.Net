using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongIdentify
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }

        [JsonPropertyName("application_id")]
        public string ApplicationId { get; set; }

        [JsonPropertyName("reconnect")]
        public bool Reconnect { get; set; }

        [JsonPropertyName("auth")]
        public string? Authentication { get; set; }

        [JsonPropertyName("tags")]
        public string[] Tags { get; set; }
    }
}
