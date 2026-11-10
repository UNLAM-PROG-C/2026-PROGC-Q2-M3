using System.Text.Json;
using System.Text.Json.Serialization;
using Rts.Shared.Geometry;

namespace Rts.Server.Configuration;

/// <summary>
/// Lee <see cref="FootprintSize"/> como <c>{"width": W, "height": H}</c>. Hace falta porque el lector JSON usa el constructor vacío de
/// los structs y no el que valida; así <c>Rts.Shared</c> sigue sin atributos JSON (contrato 00: el núcleo no conoce el formato).
/// </summary>
internal sealed class FootprintSizeJsonConverter : JsonConverter<FootprintSize>
{
    public override FootprintSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Una huella se escribe como { \"width\": W, \"height\": H }.");
        }

        int? width = null;
        int? height = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            if (name.Equals("width", StringComparison.OrdinalIgnoreCase))
            {
                width = reader.GetInt32();
            }
            else if (name.Equals("height", StringComparison.OrdinalIgnoreCase))
            {
                height = reader.GetInt32();
            }
            else
            {
                throw new JsonException($"Campo desconocido en una huella: '{name}'.");
            }
        }

        if (width is not { } w || height is not { } h)
        {
            throw new JsonException("Una huella necesita \"width\" y \"height\".");
        }

        try
        {
            return new FootprintSize(w, h);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Huella inválida ({w}x{h}): {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, FootprintSize value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}
