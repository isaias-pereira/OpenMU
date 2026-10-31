// <copyright file="CustomMoveChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.GameLogic.PlayerActions;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles custom move commands registered via the admin panel.
/// Usage: /custommove &lt;name&gt; (e.g. /custommove bar). Aliases (e.g. "/bar") are resolved as fallback by the
/// ChatMessageCommandProcessor; the plugin doesn't use "/move", so the native
/// MoveChatCommandPlugIn keeps working independently.
/// </summary>
[Guid("B7A3C9D2-E5F1-4A8B-9C7D-3E6F2A1B4C5D")]
[PlugIn]
[Display(Name = nameof(PlugInResources.CustomMoveChatCommandPlugIn_Name), Description = nameof(PlugInResources.CustomMoveChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(CommandKey, typeof(CustomMoveChatCommandArgs), CharacterStatus.Normal)]
public class CustomMoveChatCommandPlugIn : ChatCommandPlugInBase<CustomMoveChatCommandArgs>, ISupportCustomConfiguration<CustomMoveConfiguration>, ISupportDefaultCustomConfiguration
{
    /// <summary>
    /// The command key of this plugin. It must not collide with native command keys
    /// (especially not "/move", which belongs to <see cref="MoveChatCommandPlugIn"/>):
    /// the strategy provider keeps the plugin which registers first and rejects the
    /// other one, and the registration order depends on the plugin discovery order.
    /// </summary>
    public const string CommandKey = "/custommove";

    /// <inheritdoc />
    public override string Key => CommandKey;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public CustomMoveConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public object CreateDefaultConfig()
    {
        return new CustomMoveConfiguration
        {
            Enabled = true,
            Moves = new List<CustomMoveEntry>(),
        };
    }

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, CustomMoveChatCommandArgs arguments)
    {
        var config = this.Configuration;
        if (config is null || !config.Enabled)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveSystemDisabled)).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(arguments.Name))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveNameRequired)).ConfigureAwait(false);
            return;
        }

        var move = config.Moves.FirstOrDefault(m => m.Name.Equals(arguments.Name, StringComparison.OrdinalIgnoreCase));
        if (move is null)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveNotFound), arguments.Name).ConfigureAwait(false);
            return;
        }

        if (move.Map is null)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveMapNotConfigured)).ConfigureAwait(false);
            return;
        }

        // Check map entry requirements, consistent with the native warp behavior,
        // so that configured moves cannot bypass map restrictions.
        if (move.Map.TryGetRequirementError(player, out var requirementError))
        {
            await player.ShowBlueMessageAsync(requirementError).ConfigureAwait(false);
            return;
        }

        // Check that the target coordinate is walkable, so that players don't
        // get warped into walls and charged for it.
        if (!new GameMapTerrain(move.Map).WalkMap[move.X, move.Y])
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveInvalidCoordinates)).ConfigureAwait(false);
            return;
        }

        // Check level requirement
        if (player.Attributes != null && player.Attributes[Stats.Level] < move.MinLevel)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveLevelTooLow), move.MinLevel).ConfigureAwait(false);
            return;
        }

        // Check money requirement
        if (move.Cost > 0)
        {
            if (!player.TryRemoveMoney(move.Cost))
            {
                await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveNotEnoughMoney), move.Cost).ConfigureAwait(false);
                return;
            }
        }

        // Create an exit gate to the target coordinates
        // ExitGate requires a rectangular area (X1,Y1,X2,Y2), so we create a small area around the target point
        var exitGate = new ExitGate
        {
            Map = move.Map,
            X1 = move.X,
            Y1 = move.Y,
            X2 = move.X,
            Y2 = move.Y,
        };

        await player.WarpToAsync(exitGate).ConfigureAwait(false);
        await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name).ConfigureAwait(false);
    }
}