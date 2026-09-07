// <copyright file="AccountVip.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.DataModel.Entities;

using System;

/// <summary>
/// Status VIP ativo de uma conta. Relação 1-para-1 usando Shared Primary Key.
/// </summary>
[AggregateRoot]
public class AccountVip
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="AccountVip"/>.
    /// </summary>
    public AccountVip()
    {
    }

    /// <summary>
    /// Inicializa uma nova instância de <see cref="AccountVip"/> com o Id informado.
    /// </summary>
    public AccountVip(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier. Deve ser sempre o mesmo Id da <see cref="Account"/>.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Gets or sets the Id do plano VIP ativo.
    /// </summary>
    public Guid? VipPlanId { get; set; }
    
    /// <summary>
    /// Gets or sets a data de expiração do VIP.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}