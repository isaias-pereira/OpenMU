// <copyright file="CustomMoveChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;

/// <summary>
/// Arguments used by <see cref="CustomMoveChatCommandPlugIn"/>.
/// </summary>
public class CustomMoveChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the move name (e.g. "bar").
    /// </summary>
    [Argument("name")]
    public string? Name { get; set; }
}