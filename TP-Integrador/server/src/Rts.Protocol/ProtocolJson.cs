using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Protocol;

/// <summary>
/// Convenciones JSON del protocolo (RECOMENDADO D-072), compartidas por Networking y las pruebas de ida y vuelta:
/// <list type="bullet">
/// <item>Propiedades en <c>camelCase</c>.</item>
/// <item>Enums como string con el nombre del valor (<c>"Worker"</c>, <c>"Farm"</c>), nunca como número.</item>
/// <item>Las propiedades nulas se omiten al escribir.</item>
/// <item>Los eventos polimórficos llevan el discriminador <c>"type"</c> como primera propiedad.</item>
/// <item>UTF-8, un objeto por línea: el framing y el sobre (<c>ProtocolVersion</c>, <c>Type</c>, <c>MessageId</c>) los define Networking.</item>
/// </list>
/// </summary>
public static class ProtocolJson
{
    /// <summary>Opciones de solo lectura; usar siempre estas para serializar y deserializar mensajes del protocolo.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
