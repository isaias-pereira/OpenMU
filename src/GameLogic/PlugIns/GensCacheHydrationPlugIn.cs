// <copyright file="GensCacheHydrationPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Carrega o status Gens persistido do personagem para o cache em memória do
/// <see cref="GensService"/> quando o jogador entra no mundo. Necessário porque o
/// cache é volátil: ao reiniciar o servidor, quem já estava em uma Gens perderia
/// o estado até que o cache fosse reidratado no login.
/// </summary>
[PlugIn]
[Display(Name = "Gens Cache Hydration", Description = "Reidrata o cache de Gens quando o jogador entra no mundo.")]
[Guid("2C7B8C1E-1E4A-4A10-9A55-0F0E1B2C3D4E")]
public class GensCacheHydrationPlugIn : IPlayerStateChangedPlugIn
{
    /// <inheritdoc />
    public async ValueTask PlayerStateChangedAsync(Player player, State previousState, State currentState)
    {
        if (previousState != PlayerState.CharacterSelection
            || currentState != PlayerState.EnteredWorld
            || player.SelectedCharacter is not { } character)
        {
            return;
        }

        await GensService.LoadIntoCacheAsync(player.PersistenceContext, character.Id).ConfigureAwait(false);
    }
}