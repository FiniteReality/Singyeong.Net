using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Singyeong.Internal;
using Singyeong.Protocol;

namespace Singyeong.Converters
{
    internal class SingyeongTargetConverter : JsonConverter<SingyeongTarget?>
    {
        public override SingyeongTarget? Read(ref Utf8JsonReader reader,
            Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer,
            SingyeongTarget? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStartObject();

                writer.WriteString("application", value.Value.ApplicationId);
                writer.WriteBoolean("restricted", value.Value.AllowRestricted);
                writer.WriteString("key", value.Value.ConsistentHashKey);

                if (value.Value.Query != null)
                {
                    writer.WritePropertyName("ops");
                    QueryCompiler.WriteQuery(writer, value.Value.Query);
                }
                else
                {
                    writer.WriteStartArray("ops");
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
        }
    }
}