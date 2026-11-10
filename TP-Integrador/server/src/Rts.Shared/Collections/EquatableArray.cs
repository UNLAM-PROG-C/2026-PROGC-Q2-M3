using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Rts.Shared.Collections;

/// <summary>
/// Arreglo inmutable con igualdad estructural (elemento a elemento).
/// RECOMENDADO (contrato 00 §6): usarlo en records de contratos (inputs, resultados, eventos) para que dos
/// resultados con el mismo contenido sean iguales; esto habilita las pruebas de determinismo y replay
/// que comparan resultados completos. <c>default</c> se comporta como un arreglo vacío.
/// </summary>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
  private readonly ImmutableArray<T> _items;

  public EquatableArray(ImmutableArray<T> items)
  {
    _items = items.IsDefault ? ImmutableArray<T>.Empty : items;
  }

  public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

  private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

  public int Count => Items.Length;

  public bool IsEmpty => Items.IsEmpty;

  public T this[int index] => Items[index];

  public ImmutableArray<T> AsImmutableArray() => Items;

  public ReadOnlySpan<T> AsSpan() => Items.AsSpan();

  public bool Equals(EquatableArray<T> other) =>
      Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default);

  public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

  public override int GetHashCode()
  {
    var hash = new HashCode();
    foreach (T item in Items)
    {
      hash.Add(item);
    }

    return hash.ToHashCode();
  }

  public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

  IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

  public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

  public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

  public override string ToString() => $"[{string.Join(", ", Items)}]";
}

/// <summary>Fábricas de <see cref="EquatableArray{T}"/>; habilitan expresiones de colección (<c>[a, b, c]</c>).</summary>
public static class EquatableArray
{
  public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

  public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) => new(items.ToImmutableArray());
}
