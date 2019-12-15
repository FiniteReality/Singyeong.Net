using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Singyeong.Protocol
{
    internal class SingyeongPayload
    {
        [JsonPropertyName("op")]
        public SingyeongOpcode Opcode { get; set; }

        [JsonPropertyName("d")]
        public object? Payload { get; set; }

        public TaskCompletionSource<bool>? SendPromise { get; set; }
    }
}
