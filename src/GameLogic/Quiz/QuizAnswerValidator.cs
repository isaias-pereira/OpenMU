// <copyright file="QuizAnswerValidator.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Quiz;

/// <summary>
/// Normaliza y compara respuestas del Quiz. Reglas: case-insensitive, sin acentos,
/// espacios redundantes eliminados. La comparación final es contra la respuesta
/// "normalizada" almacenada con la pregunta
/// (ver <see cref="MUnique.OpenMU.DataModel.Entities.QuizAnswer.NormalizedAnswer"/>) —
/// por eso el GM debe guardar las respuestas ya normalizadas (o usar el panel, que normaliza al guardar).
/// </summary>
public static class QuizAnswerValidator
{
    /// <summary>
    /// Normaliza un texto: minúsculas, sin acentos, sin espacios redundantes y trim.
    /// </summary>
    /// <param name="input">El texto bruto.</param>
    /// <returns>El texto normalizado.</returns>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return CollapseWhitespace(RemoveAccents(input.ToLowerInvariant()));
    }

    /// <summary>
    /// Indica si <paramref name="answer"/> es una respuesta válida para una de las
    /// respuestas normalizadas dadas.
    /// </summary>
    /// <param name="answer">La respuesta del jugador (raw).</param>
    /// <param name="normalizedAcceptedAnswers">Lista de respuestas válidas ya normalizadas.</param>
    /// <returns><c>true</c>, si coincide con alguna; <c>false</c> en caso contrario.</returns>
    public static bool IsValidAnswer(string answer, IEnumerable<string> normalizedAcceptedAnswers)
    {
        var normalized = Normalize(answer);
        return normalized.Length > 0 && normalizedAcceptedAnswers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Any(a => string.Equals(a, normalized, StringComparison.Ordinal));
    }

    private static string CollapseWhitespace(string input)
    {
        var result = input.Trim();
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        return result;
    }

    private static string RemoveAccents(string input)
    {
        var result = input;
        foreach (var (accented, plain) in AccentMap)
        {
            if (result.Contains(accented))
            {
                result = result.Replace(accented, plain);
            }
        }

        return result;
    }

    private static readonly Dictionary<char, char> AccentMap = new()
    {
        { 'á', 'a' }, { 'à', 'a' }, { 'â', 'a' }, { 'ã', 'a' }, { 'ä', 'a' }, { 'å', 'a' }, { 'ā', 'a' },
        { 'é', 'e' }, { 'è', 'e' }, { 'ê', 'e' }, { 'ë', 'e' }, { 'ē', 'e' }, { 'ę', 'e' },
        { 'í', 'i' }, { 'ì', 'i' }, { 'î', 'i' }, { 'ï', 'i' },
        { 'ó', 'o' }, { 'ò', 'o' }, { 'ô', 'o' }, { 'õ', 'o' }, { 'ö', 'o' }, { 'ø', 'o' },
        { 'ú', 'u' }, { 'ù', 'u' }, { 'û', 'u' }, { 'ü', 'u' }, { 'ū', 'u' },
        { 'ý', 'y' }, { 'ÿ', 'y' },
        { 'ç', 'c' }, { 'ć', 'c' }, { 'č', 'c' },
        { 'ñ', 'n' }, { 'ń', 'n' },
        { 'š', 's' }, { 'ś', 's' },
        { 'ž', 'z' }, { 'ź', 'z' }, { 'ż', 'z' },
        { 'đ', 'd' }, { 'ğ', 'g' }, { 'ł', 'l' }, { 'ť', 't' },
    };
}