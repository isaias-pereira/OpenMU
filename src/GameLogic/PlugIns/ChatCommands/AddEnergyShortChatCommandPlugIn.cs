// <copyright file="AddEnergyShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short command to add energy stat points.
/// </summary>
[Guid("3C4D5E6F-7A8B-4C9D-0E1F-2A3B4C5D6E7F")]
[PlugIn]
[Display(Name = nameof(PlugInResources.AddEnergyShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.AddEnergyShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class AddEnergyShortChatCommandPlugIn : AddEnergyStatChatCommandPlugIn, IDisabledByDefault
{
    private const string Command = "/e";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public override async ValueTask HandleCommandAsync(Player player, string command)
    {
        command = "/addene" + command.Substring(Command.Length);
        await base.HandleCommandAsync(player, command).ConfigureAwait(false);
    }
}