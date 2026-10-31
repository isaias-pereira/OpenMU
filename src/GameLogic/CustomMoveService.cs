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
        if (configuration?.Moves is null)
        {
            return null;
        }

        return configuration.Moves.FirstOrDefault(m =>
            string.Equals(m.EffectiveCommand, command, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets warnings for duplicated move names or commands. The runtime uses the
    /// first matching move, so duplicates would silently shadow the others.
    /// </summary>
    /// <param name="configuration">The custom move configuration.</param>
    /// <returns>The list of warning messages; empty if there are no duplicates.</returns>
    public static List<string> GetDuplicateCommandWarnings(CustomMoveConfiguration? configuration)
    {
        var warnings = new List<string>();
        if (configuration?.Moves is null)
        {
            return warnings;
        }

        foreach (var duplicates in configuration.Moves
                     .GroupBy(m => m.EffectiveCommand, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            warnings.Add($"The command '{duplicates.Key}' is configured for multiple moves ({string.Join(", ", duplicates.Select(m => m.Name))}). Only the first one is used.");
        }

        foreach (var duplicates in configuration.Moves
                     .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            warnings.Add($"The move name '{duplicates.Key}' is used multiple times. Only the first entry is used.");
        }

        return warnings;
    }
}