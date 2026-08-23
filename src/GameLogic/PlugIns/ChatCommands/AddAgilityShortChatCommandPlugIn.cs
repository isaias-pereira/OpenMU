// <copyright file="AddAgilityShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short command to add agility stat points.
/// </summary>
[Guid("1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C5D")]
[PlugIn]
[Display(Name = nameof(PlugInResources.AddAgilityShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.AddAgilityShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class AddAgilityShortChatCommandPlugIn : AddAgilityStatChatCommandPlugIn, IDisabledByDefault
{
    private const string Command = "/a";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public override async ValueTask HandleCommandAsync(Player player, string command)
    {
        command = "/addagi" + command.Substring(Command.Length);
        await base.HandleCommandAsync(player, command).ConfigureAwait(false);
    }
}