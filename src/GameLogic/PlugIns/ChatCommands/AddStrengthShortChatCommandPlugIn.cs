// <copyright file="AddStrengthShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short command to add strength stat points.
/// </summary>
[Guid("8F2A1B3C-4D5E-4F60-9A71-B2C3D4E5F607")]
[PlugIn]
[Display(Name = nameof(PlugInResources.AddStrengthShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.AddStrengthShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class AddStrengthShortChatCommandPlugIn : AddStrengthStatChatCommandPlugIn, IDisabledByDefault
{
    private const string Command = "/f";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public override async ValueTask HandleCommandAsync(Player player, string command)
    {
        command = "/addstr" + command.Substring(Command.Length);
        await base.HandleCommandAsync(player, command).ConfigureAwait(false);
    }
}