// <copyright file="VipCacheHydrationPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Carrega o status VIP persistido da conta para o cache em memória do <see cref="VipService"/>
/// quando o jogador entra no mundo. Necessário porque o cache é volátil: ao reiniciar o
/// servidor, quem já era VIP perderia o bônus de drop até o cache ser reidratado no login.
/// </summary>
[PlugIn]
[Display(Name = "VIP Cache Hydration", Description = "Reidrata o cache de VIP quando o jogador entra no mundo.")]
[Guid("A2B3C4D5-E6F7-4890-ABCD-1234567890AB")]
public class VipCacheHydrationPlugIn : IPlayerStateChangedPlugIn
{
    /// <inheritdoc />
    public async ValueTask PlayerStateChangedAsync(Player player, State previousState, State currentState)
    {
        if (previousState != PlayerState.CharacterSelection
            || currentState != PlayerState.EnteredWorld
            || player.Account is not { } account)
        {
            return;
        }

        await VipService.LoadIntoCacheAsync(player.PersistenceContext, account.Id).ConfigureAwait(false);
    }
}
