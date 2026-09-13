// <copyright file="AccountVault.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System;

/// <summary>
/// One numbered vault (warehouse) of an <see cref="Account"/>.
/// Each vault owns its own <see cref="ItemStorage"/>, so items and money are separated per vault.
/// </summary>
[AggregateRoot]
public class AccountVault
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AccountVault"/> class.
    /// </summary>
    public AccountVault()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountVault"/> with the given id.
    /// </summary>
    /// <param name="id">The identifier.</param>
    public AccountVault(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the vault number, 1-based (1 = first/legacy vault).
    /// </summary>
    public int Number { get; set; } = 1;

    /// <summary>
    /// Gets or sets the storage which contains the items and money of this vault.
    /// </summary>
    [MemberOfAggregate]
    [HiddenAtCreation]
    public virtual ItemStorage? Storage { get; set; }
}
