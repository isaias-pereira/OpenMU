// <copyright file="CustomMoveEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.DataModel.Entities;

using System.ComponentModel.DataAnnotations;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// A custom move entry which defines a warp to a specific map and coordinates,
/// registered via the admin panel.
/// </summary>
[AggregateRoot]
public class CustomMoveEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomMoveEntry"/> class.
    /// </summary>
    public CustomMoveEntry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomMoveEntry"/> class with the specified id.
    /// </summary>
    public CustomMoveEntry(Guid id) : this()
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique name of the move (used as chat command argument).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target map.
    /// </summary>
    public virtual GameMapDefinition? Map { get; set; }

    /// <summary>
    /// Gets or sets the chat command to trigger this move (e.g. "bar" or "/bar").
    /// If not set, defaults to "/{Name}".
    /// </summary>
    [Display(Name = "Command", Description = "Chat command to trigger this move (e.g. bar or /bar). Leave empty to use /{Name}.")]
    public string? Command { get; set; }

    /// <summary>
    /// Gets the effective command for this move, always with a single leading slash
    /// (e.g. "/bar"). The configured <see cref="Command"/> is tolerated with or
    /// without a leading slash and with surrounding whitespace.
    /// </summary>
    public string EffectiveCommand
    {
        get
        {
            var command = this.Command?.Trim().TrimStart('/');
            return $"/{(string.IsNullOrEmpty(command) ? this.Name : command)}";
        }
    }

    /// <summary>
    /// Gets or sets the X coordinate.
    /// </summary>
    public byte X { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate.
    /// </summary>
    public byte Y { get; set; }

    /// <summary>
    /// Gets or sets the zen cost to use this move (0 = free).
    /// </summary>
    public int Cost { get; set; }

    /// <summary>
    /// Gets or sets the minimum level required to use this move.
    /// </summary>
    public int MinLevel { get; set; }
}