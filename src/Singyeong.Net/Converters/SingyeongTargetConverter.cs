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
            SingyeongTarget? target, JsonSerializerOptions options)
        {
            if (target.HasValue)
            {
                writer.WriteStartObject();

                if (target.Value.ApplicationId != null)
                    writer.WriteString("application",
                        target.Value.ApplicationId);
                else if (target.Value.ApplicationTags != null)
                {
                    writer.WriteStartArray("application");
                    foreach (var value in target.Value.ApplicationTags)
                        writer.WriteStringValue(value);
                    writer.WriteEndArray();
                }
                writer.WriteBoolean("restricted", target.Value.AllowRestricted);
                writer.WriteString("key", target.Value.ConsistentHashKey);

                if (target.Value.Query != null)
                {
                    writer.WritePropertyName("ops");
                    QueryCompiler.WriteQuery(writer, target.Value.Query);
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
