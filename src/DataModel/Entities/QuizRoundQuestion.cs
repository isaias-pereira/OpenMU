// <copyright file="QuizRoundQuestion.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Representa uma pergunta específica dentro de uma rodada de Quiz,
/// com sua sequência, timing e o vencedor (se houver).
/// </summary>
[AggregateRoot]
public class QuizRoundQuestion
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the identifier of the <see cref="QuizRound"/> this question belongs to.
    /// </summary>
    public Guid RoundId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the <see cref="QuizQuestion"/> that was asked.
    /// </summary>
    public Guid QuestionId { get; set; }

    /// <summary>
    /// Gets or sets the sequence number of this question in the round (0-based).
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Gets or sets the date/time when this question was presented.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date/time when this question ended (answered or timed out).
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the winning account, if any.
    /// </summary>
    public Guid? WinnerAccountId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the winning character, if any.
    /// </summary>
    public Guid? WinnerCharacterId { get; set; }
}