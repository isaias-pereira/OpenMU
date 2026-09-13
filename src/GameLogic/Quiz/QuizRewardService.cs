// <copyright file="QuizRewardService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Quiz;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Concede la recompensa en WCoin de una respuesta correcta del Quiz y registra el
/// histórico en <see cref="QuizWinner"/>. Reutiliza <see cref="WCoinService"/> (no se
/// duplica lógica de cartera) y escribe la transacción de WCoin y el registro de ganador
/// en el mismo contexto de persistencia del jugador.
/// </summary>
/// <remarks>
/// El flujo de "sólo el primero gana" vive en <see cref="QuizEventService"/> (elección
/// síncrona del ganador antes de cualquier await). Aquí solo se persiste.
/// El valor de la recompensa SIEMPRE proviene de la configuración del servidor
/// (<see cref="QuizConfiguration.RewardAmount"/>) y la moneda de
/// <see cref="QuizConfiguration.RewardCurrency"/> — nunca del cliente.
/// </remarks>
public static class QuizRewardService
{
    /// <summary>
    /// Título usado en la descripción de la transacción de WCoin.
    /// </summary>
    public static const string TransactionDescription = "Quiz";

    /// <summary>
    /// Credita X WCoin a la cuenta, registra el <see cref="QuizWinner"/> y cierra la
    /// pregunta de la ronda con el ganador.
    /// </summary>
    /// <param name="context">Contexto de persistencia del jugador.</param>
    /// <param name="accountId">Id de la cuenta ganadora.</param>
    /// <param name="characterId">Id del personaje ganador.</param>
    /// <param name="roundQuestion"><see cref="QuizRoundQuestion"/> actual (se marca ganador y fin).</param>
    /// <param name="currency">Moneda de la recompensa (WCoinC o WCoinP).</param>
    /// <param name="amount">Monto en WCoin.</param>
    /// <returns><c>true</c>, si se registró y acreditó correctamente.</returns>
    public static async ValueTask<bool> RewardAsync(IContext context, Guid accountId, Guid characterId,
        QuizRoundQuestion roundQuestion, QuizRewardCurrency currency, long amount)
    {
        if (accountId is null || roundQuestion is null || amount <= 0)
        {
            return false;
        }

        try
        {
            await WCoinService.CreditAsync(context, accountId, amount, WCoinTransactionType.Reward, $"{TransactionDescription} - {roundQuestion.Sequence + 1}").ConfigureAwait(false);

            var winner = context.CreateNew<QuizWinner>();
            winner.RoundQuestionId = roundQuestion.Id;
            winner.AccountId = accountId;
            winner.CharacterId = characterId;
            winner.Currency = currency;
            winner.Amount = amount;
            winner.CreatedAt = DateTime.UtcNow;

            roundQuestion.WinnerAccountId = accountId;
            roundQuestion.WinnerCharacterId = characterId;
            roundQuestion.EndedAt = DateTime.UtcNow;

            await context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            context.Dispose();
            throw ex;
        }
    }
}