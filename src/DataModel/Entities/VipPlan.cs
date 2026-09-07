// <copyright file="VipPlan.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.DataModel.Entities;

using System;

/// <summary>
/// Configuração de um plano VIP. Permite escalabilidade futura (EXP, Zen, etc).
/// </summary>
[AggregateRoot]
public class VipPlan
{
    /// <summary>
    /// Inicializa uma nova instância de <see cref="VipPlan"/>.
    /// </summary>
    public VipPlan()
    {
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public float DropChanceMultiplier { get; set; } = 1.0f;
    public bool IsActive { get; set; } = true;
}