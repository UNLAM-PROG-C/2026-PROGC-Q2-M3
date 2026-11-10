using System.Text.RegularExpressions;

namespace Rts.Shared.Errors;

/// <summary>
/// Código de error tipado y extensible (D-007). Formato obligatorio <c>Area.Nombre</c> en PascalCase
/// (p. ej. <c>Common.NotOwner</c>, <c>Economy.InsufficientWood</c>). Shared define los transversales en
/// <see cref="CommonErrors"/>; cada módulo declara los suyos en su propio proyecto con su prefijo de área.
/// Se serializa a JSON como string. La taxonomía exhaustiva sigue PENDIENTE.
/// </summary>
public readonly partial record struct ErrorCode
{
  private readonly string? _value;

  public ErrorCode(string value)
  {
    ArgumentNullException.ThrowIfNull(value);
    if (!FormatRegex().IsMatch(value))
    {
      throw new ArgumentException(
          $"Código de error inválido '{value}'. Formato esperado: Area.Nombre (PascalCase).",
          nameof(value));
    }

    _value = value;
  }

  /// <summary>Texto del código; vacío solo para <c>default(ErrorCode)</c>, que no es un código válido.</summary>
  public string Value => _value ?? string.Empty;

  /// <summary>Área (prefijo) del código, p. ej. <c>Common</c>.</summary>
  public string Area => _value is null ? string.Empty : _value[.._value.IndexOf('.', StringComparison.Ordinal)];

  public bool IsDefault => _value is null;

  public override string ToString() => Value;

  [GeneratedRegex("^[A-Z][A-Za-z0-9]*\\.[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
  private static partial Regex FormatRegex();
}
