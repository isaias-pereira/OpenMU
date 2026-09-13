// <copyright file="CustomMoveConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Configuration for the <see cref="CustomMoveChatCommandPlugIn"/>.
/// Stores the registered moves which can be managed via the admin panel.
/// </summary>
public class CustomMoveConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomMoveConfiguration"/> class.
    /// </summary>
    public CustomMoveConfiguration()
    {
        this.Moves = new List<CustomMoveEntry>();
    }

    /// <summary>
    /// Gets or sets the moves registered in the system.
    /// </summary>
    [Display(Name = "Moves", Description = "List of registered custom moves.")]
    public List<CustomMoveEntry> Moves { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the move system is enabled.
    /// </summary>
    [Display(Name = "Enabled", Description = "Enables or disables the custom move system.")]
    public bool Enabled { get; set; } = true;
}