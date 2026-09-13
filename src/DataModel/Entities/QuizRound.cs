// <copyright file="QuizRound.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Status de uma rodada de Quiz.
/// </summary>
public enum QuizRoundStatus
{
    /// <summary>
    /// A rodada está em andamento.
    /// </summary>
    Active,

    /// <summary>
    /// A rodada foi concluída.
    /// </summary>
    Completed,

    /// <summary>
    /// A rodada foi cancelada.
    /// </summary>
    Cancelled,
}

/// <summary>
/// Representa uma rodada completa do evento Quiz (conjunto de perguntas).
/// </summary>
[AggregateRoot]
public class QuizRound
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the date/time when this round started.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date/time when this round ended.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of questions in this round.
    /// </summary>
    public int QuestionCount { get; set; }

    /// <summary>
    /// Gets or sets the status of this round.
    /// </summary>
    public QuizRoundStatus Status { get; set; } = QuizRoundStatus.Active;
}