// <copyright file="UnsafeContextRetirement.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Retira do jogo uma sessao cujo <see cref="IContext"/> ficou possivelmente sujo, para que
/// nenhum <c>SaveChangesAsync</c> posterior persista o que sobrou.
/// </summary>
/// <remarks>
/// Runs the disconnect pipeline without saving and attempts context disposal even when
/// cleanup fails or the state transition is refused. Disposal must invalidate persistence
/// in the configured context implementation; a disposal failure is never reported as success.
/// </remarks>
public sealed class UnsafeContextRetirement
{
    private readonly ILogger<UnsafeContextRetirement> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsafeContextRetirement"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public UnsafeContextRetirement(ILogger<UnsafeContextRetirement> logger)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retira a sessao do fluxo do jogo sem salvar o contexto possivelmente sujo.
    /// </summary>
    /// <param name="player">O jogador cuja sessao deve ser retirada.</param>
    /// <param name="reason">O motivo, usado no log.</param>
    /// <returns>
    /// <c>true</c> se a sessao saiu do fluxo <b>e</b> o contexto sujo foi invalidado; <c>false</c>
    /// se alguma etapa falhou. O descarte do contexto e tentado em todos os caminhos.
    /// </returns>
    public async ValueTask<bool> RetireAsync(Player player, string reason)
    {
        ArgumentNullException.ThrowIfNull(player);

        var disconnected = false;
        try
        {
            disconnected = await player.DisconnectWithoutSavingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Preserve the purchase/gift's RollbackIncomplete result or original cancellation.
            this._logger.LogCritical(
                exception,
                "Could not complete retirement of {Player} ({Reason}): the disconnect pipeline threw. Context disposal will still be attempted.",
                player.Name,
                reason);
        }

        // The hook is virtual, so verify disposal independently of whether it ran.
        var invalidated = this.InvalidateContext(player, reason);
        if (!disconnected)
        {
            this._logger.LogCritical(
                "Retirement of {Player} ({Reason}) did not complete. State: {State}; context disposal completed: {Invalidated}. Manual inspection is required.",
                player.Name,
                reason,
                player.PlayerState.CurrentState,
                invalidated);
            return false;
        }

        if (!invalidated)
        {
            return false;
        }

        var currentState = player.PlayerState.CurrentState;
        if (!currentState.IsDisconnectedOrFinished())
        {
            this._logger.LogCritical(
                "Retiring the session of {Player} ({Reason}) did not reach a disconnected state: it is {State}. The context is disposed, but the session was not fully retired.",
                player.Name,
                reason,
                currentState);
            return false;
        }

        this._logger.LogError(
            "The session of {Player} was retired ({Reason}): it was removed from the game and its context was disposed without being saved, so the dirty state can no longer be persisted.",
            player.Name,
            reason);
        return true;
    }

    private bool InvalidateContext(Player player, string reason)
    {
        try
        {
            player.InvalidatePersistenceContext();
            return true;
        }
        catch (Exception exception)
        {
            this._logger.LogCritical(
                exception,
                "Could not dispose the persistence context of {Player} ({Reason}). Persistence containment is unconfirmed; manual inspection is required.",
                player.Name,
                reason);
            return false;
        }
    }
}
