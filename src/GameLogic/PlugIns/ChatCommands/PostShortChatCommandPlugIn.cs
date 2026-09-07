// <copyright file="PostShortChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which handles the short "/p" variant of the post command.
/// It inherits the behavior of <see cref="PostChatCommandPlugIn"/>, including the shared cooldown,
/// so players can't post twice as often by alternating between "/post" and "/p".
/// </summary>
/// <remarks>
/// Because the plugin system keeps one configuration per plugin type, this command has its own
/// configuration entry. Cooldown, message length and texts therefore have to be maintained for
/// both commands - only the cooldown timestamps themselves are shared.
/// </remarks>
[Guid("3F8B6D14-9C27-4A51-B0E3-7D2A5C816F49")]
[PlugIn]
[Display(Name = nameof(PlugInResources.PostShortChatCommandPlugIn_Name), Description = nameof(PlugInResources.PostShortChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class PostShortChatCommandPlugIn : PostChatCommandPlugIn
{
    private const string Command = "/p";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => MinimumStatus;
}
