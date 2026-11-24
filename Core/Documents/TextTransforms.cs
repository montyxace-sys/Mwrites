using System.Globalization;

namespace Mwrites.Core.Documents;

/// <summary>Modos disponibles al cambiar el caso del texto seleccionado.</summary>
public enum TextCase
{
    Upper,
    Lower,
    Title,
    Sentence
}

/// <summary>
/// Transformaciones de texto independientes de la interfaz. Se comparten entre la
/// edición WPF y la edición portable para que ambas produzcan exactamente el mismo
/// resultado.
/// </summary>
public static class TextTransforms
{
    public static string Apply(string? text, TextCase mode) => mode switch
    {
        TextCase.Upper => ToUpper(text),
        TextCase.Lower => ToLower(text),
        TextCase.Title => ToTitle(text),
        TextCase.Sentence => ToSentence(text),
        _ => text ?? string.Empty
    };

    public static string ToUpper(string? text) => (text ?? string.Empty).ToUpperInvariant();

    public static string ToLower(string? text) => (text ?? string.Empty).ToLowerInvariant();

    /// <summary>Pone en mayúscula la primera letra de cada palabra.</summary>
    public static string ToTitle(string? text, CultureInfo? culture = null)
    {
        var source = text ?? string.Empty;
        if (source.Length == 0) return source;

        culture ??= CultureInfo.CurrentCulture;
        var chars = source.ToCharArray();
        var startOfWord = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetterOrDigit(chars[i]))
            {
                if (startOfWord) chars[i] = char.ToUpper(chars[i], culture);
                startOfWord = false;
            }
            else
            {
                startOfWord = true;
            }
        }
        return new string(chars);
    }

    /// <summary>
    /// Convierte a minúsculas y escribe en mayúscula la primera letra de cada frase,
    /// respetando los signos de apertura españoles (¿ y ¡).
    /// </summary>
    public static string ToSentence(string? text, CultureInfo? culture = null)
    {
        var source = text ?? string.Empty;
        if (source.Length == 0) return source;

        culture ??= CultureInfo.CurrentCulture;
        var lower = source.ToLower(culture);
        var chars = lower.ToCharArray();
        var startOfSentence = true;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsLetterOrDigit(c))
            {
                if (startOfSentence) chars[i] = char.ToUpper(c, culture);
                startOfSentence = false;
            }
            else if (c is '¿' or '¡' or '\n' or '\r')
            {
                // Los signos de apertura no rompen la frase; los saltos de línea sí.
                if (c is '\n' or '\r') startOfSentence = true;
            }
            else if (c is '.' or '!' or '?')
            {
                startOfSentence = true;
            }
        }
        return new string(chars);
    }
}
