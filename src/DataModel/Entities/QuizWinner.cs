// <copyright file="QuizWinner.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Histórico de vitórias e recompensas concedidas no evento Quiz.
/// Registro auditável de quem ganhou e quanto recebeu.
/// </summary>
[AggregateRoot]
public class QuizWinner
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the identifier of the <see cref="QuizRoundQuestion"/> that foi respondida.
    /// </summary>
    public Guid RoundQuestionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the account that won.
    /// </summary>
    [Required]
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the character that won.
    /// </summary>
    [Required]
    public Guid CharacterId { get; set; }

    /// <summary>
    /// Gets or sets the currency that was awarded.
    /// </summary>
    public QuizRewardCurrency Currency { get; set; }

    /// <summary>
    /// Gets or sets the amount awarded.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// Gets or sets the date/time when the reward was granted.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}