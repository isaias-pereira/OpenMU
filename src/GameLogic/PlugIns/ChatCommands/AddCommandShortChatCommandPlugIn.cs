// <copyright file="AddCommandShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short command to add command stat points.
/// </summary>
[Guid("4D5E6F7A-8B9C-4D0E-1F2A-3B4C5D6E7F8A")]
[PlugIn]
[Display(Name = nameof(PlugInResources.AddCommandShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.AddCommandShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class AddCommandShortChatCommandPlugIn : AddCommandStatChatCommandPlugIn, IDisabledByDefault
{
    private const string Command = "/c";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public override async ValueTask HandleCommandAsync(Player player, string command)
    {
        command = "/addcmd" + command.Substring(Command.Length);
        await base.HandleCommandAsync(player, command).ConfigureAwait(false);
    }
}