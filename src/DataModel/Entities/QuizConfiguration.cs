// <copyright file="QuizConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Moeda utilizada como recompensa do Quiz.
/// </summary>
public enum QuizRewardCurrency
{
    /// <summary>
    /// WCoinC (moeda corrente).
    /// </summary>
    WCoinC,

    /// <summary>
    /// WCoinP (moeda premium).
    /// </summary>
    WCoinP,
}

/// <summary>
/// Configuração global do evento Quiz. Controla o scheduler, a quantidade de perguntas,
/// o tempo de resposta, os intervalos e a premiação em WCoin.
/// </summary>
[AggregateRoot]
public class QuizConfiguration
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizConfiguration"/>.
    /// </summary>
    public QuizConfiguration()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="QuizConfiguration"/> com o Id informado.
    /// </summary>
    public QuizConfiguration(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier. Single-row configuration (Id = 00000000-0000-0000-0000-000000000001).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the quiz event is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of questions per event round.
    /// </summary>
    public int QuestionsPerEvent { get; set; } = 5;

    /// <summary>
    /// Gets or sets the time in seconds for each question.
    /// </summary>
    public int AnswerTimeSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the minimum interval in minutes between events.
    /// </summary>
    public int MinIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum interval in minutes between events.
    /// </summary>
    public int MaxIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the cooldown in milliseconds between answers from the same player.
    /// </summary>
    public int AnswerCooldownMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of wins per player per event (0 = unlimited).
    /// </summary>
    public int MaxWinsPerEvent { get; set; } = 0;

    /// <summary>
    /// Gets or sets the reward currency (WCoinC or WCoinP).
    /// </summary>
    public QuizRewardCurrency RewardCurrency { get; set; } = QuizRewardCurrency.WCoinC;

    /// <summary>
    /// Gets or sets the reward amount in WCoin.
    /// </summary>
    public long RewardAmount { get; set; } = 150;

    /// <summary>
    /// Gets or sets the date/time when this configuration was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}