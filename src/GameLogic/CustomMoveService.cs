// <copyright file="CustomMoveService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Central service for the custom move system. Provides lookup for
/// registered moves by their effective command (e.g. "/bar").
/// </summary>
public static class CustomMoveService
{
    /// <summary>
    /// Gets a registered move by its effective command (e.g. "/bar").
    /// Reads from the <see cref="CustomMoveChatCommandPlugIn"/> configuration.
    /// </summary>
    /// <param name="command">The chat command.</param>
    /// <param name="configuration">The custom move configuration.</param>
    /// <returns>The move entry; null if not found.</returns>
    public static CustomMoveEntry? GetMove(string command, CustomMoveConfiguration configuration)
    {
        if (configuration.Moves is null)
        {
            return null;
        }

        return configuration.Moves.FirstOrDefault(m =>
            string.Equals(m.EffectiveCommand, command, StringComparison.OrdinalIgnoreCase));
    }
}