using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongBroadcast
    {
        [JsonPropertyName("target")]
        public SingyeongTarget? Target { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }

        [JsonPropertyName("payload")]
        public object Payload { get; set; }
    }
}