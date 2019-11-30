using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal struct SingyeongInvalid
    {
        [JsonPropertyName("error")]
        public string Error { get; set; }
    }
}