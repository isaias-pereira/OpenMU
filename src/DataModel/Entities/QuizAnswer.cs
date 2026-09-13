// <copyright file="QuizAnswer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// A valid answer for a quiz question. A question can have multiple valid answers
/// (e.g. "Brasilia", "Brasília", "brasilia"). The answer is stored normalized
/// (uppercase, without accents and without extra spaces) for comparison.
/// </summary>
[AggregateRoot]
public class QuizAnswer
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizAnswer"/>.
    /// </summary>
    public QuizAnswer()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizAnswer"/> com o Id informado.
    /// </summary>
    public QuizAnswer(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the <see cref="QuizQuestion"/> this answer belongs to.
    /// </summary>
    public Guid QuestionId { get; set; }

    /// <summary>
    /// Gets or sets the answer as originally typed by the GM (display reference).
    /// </summary>
    public string OriginalAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized answer (uppercase, without accents, trimmed)
    /// used for comparison with player input.
    /// </summary>
    [Required]
    public string NormalizedAnswer { get; set; } = string.Empty;
}