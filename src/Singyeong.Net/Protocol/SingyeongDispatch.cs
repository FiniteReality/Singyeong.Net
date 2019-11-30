using System.Text.Json.Serialization;

namespace Singyeong.Protocol
{
    internal class SingyeongDispatch : SingyeongPayload
    {
        public SingyeongDispatch()
        {
            Opcode = SingyeongOpcode.Dispatch;
        }

        [JsonPropertyName("t")]
        public string? DispatchType { get; set; }
    }
}