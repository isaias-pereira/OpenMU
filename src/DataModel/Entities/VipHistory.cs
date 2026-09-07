// <copyright file="VipHistory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.DataModel.Entities;

using System;

/// <summary>
/// Registro imutável (append-only) de ativações de VIP para auditoria.
/// </summary>
[AggregateRoot]
public class VipHistory
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="VipHistory"/>.
    /// </summary>
    public VipHistory()
    {
    }

    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid VipPlanId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}