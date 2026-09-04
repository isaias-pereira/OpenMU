// <copyright file="DollarNoticeChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the "/$" command by sending a golden notice message to all players.
/// It inherits the behavior of <see cref="NoticeChatCommandPlugIn"/>.
/// </summary>
[Guid("7A3E9C21-4B76-4D76-8CE1-69B712B65E6C")]
[PlugIn]
[Display(Name = nameof(PlugInResources.DollarNoticeChatCommandPlugIn_Name), Description = nameof(PlugInResources.DollarNoticeChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(CommandKey, null, MinimumStatus)]
public class DollarNoticeChatCommandPlugIn : NoticeChatCommandPlugIn
{
    private const string CommandKey = "/$";

    private const CharacterStatus MinimumStatus = CharacterStatus.GameMaster;

    /// <inheritdoc />
    public override string Key => CommandKey;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;
}
