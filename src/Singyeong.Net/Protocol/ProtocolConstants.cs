using System.Text;

namespace Singyeong.Protocol
{
    internal static class ProtocolConstants
    {
        public static byte[] OpcodePropertyName
            = Encoding.UTF8.GetBytes("op");

        public static byte[] DataPropertyName
            = Encoding.UTF8.GetBytes("d");

        public static byte[] TimestampPropertyName
            = Encoding.UTF8.GetBytes("ts");

        public static byte[] EventTypePropertyName
            = Encoding.UTF8.GetBytes("t");

        public static byte[] UpdateMetadataEventType
            = Encoding.UTF8.GetBytes("UPDATE_METADATA");

        public static byte[] SendEventType
            = Encoding.UTF8.GetBytes("SEND");

        public static byte[] BroadcastEventType
            = Encoding.UTF8.GetBytes("BROADCAST");
    }
}