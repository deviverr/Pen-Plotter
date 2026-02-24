using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlotterControl.Models
{
    [JsonConverter(typeof(PlotterPointConverter))]
    public struct PlotterPoint
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        [JsonConstructor]
        public PlotterPoint(double x, double y, double z = 0.0)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public class PlotterPointConverter : JsonConverter<PlotterPoint>
    {
        public override PlotterPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.Read();
                double x = reader.GetDouble();
                reader.Read();
                double y = reader.GetDouble();
                double z = 0.0;
                reader.Read();
                if (reader.TokenType == JsonTokenType.Number)
                {
                    z = reader.GetDouble();
                    reader.Read(); // consume EndArray
                }
                return new PlotterPoint(x, y, z);
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                double x = 0, y = 0, z = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string prop = reader.GetString();
                        reader.Read();
                        switch (prop?.ToUpperInvariant())
                        {
                            case "X": x = reader.GetDouble(); break;
                            case "Y": y = reader.GetDouble(); break;
                            case "Z": z = reader.GetDouble(); break;
                        }
                    }
                }
                return new PlotterPoint(x, y, z);
            }
            throw new JsonException($"Unexpected token {reader.TokenType} when parsing PlotterPoint");
        }

        public override void Write(Utf8JsonWriter writer, PlotterPoint value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteEndObject();
        }
    }
}
