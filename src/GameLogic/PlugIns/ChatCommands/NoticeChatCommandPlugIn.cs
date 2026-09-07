// <copyright file="NoticeChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles post commands by sending a golden notice message to all players.
/// </summary>
[Guid("2BFC9464-4B76-4D76-8CE1-69B712B65E6C")]
[PlugIn]
[Display(Name = nameof(PlugInResources.NoticeChatCommandPlugIn_Name), Description = nameof(PlugInResources.NoticeChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(CommandKey, null, MinimumStatus)]
public class NoticeChatCommandPlugIn : IChatCommandPlugIn
{
    private const string CommandKey = "/goldnotice";

    private const CharacterStatus MinimumStatus = CharacterStatus.GameMaster;

    /// <inheritdoc />
    public virtual string Key => CommandKey;

    /// <inheritdoc />
    public virtual CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var message = command.Length > this.Key.Length ? command[this.Key.Length..].Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await player.GameContext.SendGlobalMessageAsync(message, Interfaces.MessageType.GoldenCenter).ConfigureAwait(false);
    }
}
