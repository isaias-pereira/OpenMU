// <copyright file="AddVitalityShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short command to add vitality stat points.
/// </summary>
[Guid("2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D6E")]
[PlugIn]
[Display(Name = nameof(PlugInResources.AddVitalityShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.AddVitalityShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class AddVitalityShortChatCommandPlugIn : AddVitalityStatChatCommandPlugIn, IDisabledByDefault
{
    private const string Command = "/v";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public override async ValueTask HandleCommandAsync(Player player, string command)
    {
        command = "/addvit" + command.Substring(Command.Length);
        await base.HandleCommandAsync(player, command).ConfigureAwait(false);
    }
}