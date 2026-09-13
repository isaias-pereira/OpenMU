// <copyright file="QuizQuestion.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Dificuldade de uma pergunta do Quiz.
/// </summary>
public enum QuizDifficulty
{
    /// <summary>
    /// Pergunta fácil.
    /// </summary>
    Easy = 1,

    /// <summary>
    /// Pergunta de dificuldade média.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Pergunta difícil.
    /// </summary>
    Hard = 3,
}

/// <summary>
/// Uma pergunta do evento Quiz. Armazenada no PostgreSQL para gerenciamento
/// pelo painel GM (adicionar, editar, ativar/desativar).
/// </summary>
[AggregateRoot]
public class QuizQuestion
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizQuestion"/>.
    /// </summary>
    public QuizQuestion()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizQuestion"/> com o Id informado.
    /// </summary>
    public QuizQuestion(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the text of the question shown to players.
    /// </summary>
    [Required]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category of the question (e.g. "Geography", "Math", "MU Online").
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the difficulty level of the question.
    /// </summary>
    public QuizDifficulty Difficulty { get; set; } = QuizDifficulty.Medium;

    /// <summary>
    /// Gets or sets a value indicating whether this question is active and can be selected.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the date/time when this question was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date/time when this question was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}