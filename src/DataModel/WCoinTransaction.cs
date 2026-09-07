// <copyright file="WCoinTransaction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// O tipo de uma movimentação de WCOIN.
/// </summary>
public enum WCoinTransactionType
{
    /// <summary>
    /// Troca de outra moeda (ex.: Gold Free) por WCOIN.
    /// </summary>
    Exchange,

    /// <summary>
    /// Compra de um item/benefício na loja usando WCOIN.
    /// </summary>
    Purchase,

    /// <summary>
    /// Ajuste manual feito por um administrador (bônus, correção, estorno).
    /// </summary>
    AdminAdjust,
}

/// <summary>
/// Um registro imutável de uma movimentação de WCOIN. Só é feito INSERT aqui — nunca
/// UPDATE nem DELETE. É esse extrato que permite auditar, corrigir bugs e provar o
/// saldo de qualquer conta em qualquer momento do passado.
/// </summary>
[AggregateRoot]
public class WCoinTransaction
{
    /// <summary>
    /// Gets or sets the identifier desta transação.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets o Id da <see cref="Account"/> à qual esta movimentação pertence.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets o tipo da movimentação.
    /// </summary>
    public WCoinTransactionType Type { get; set; }

    /// <summary>
    /// Gets or sets o valor movimentado. Sempre positivo — o <see cref="Type"/> indica
    /// se foi um crédito ou um débito.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// Gets or sets o saldo resultante logo após esta movimentação (facilita auditoria
    /// sem precisar reprocessar o extrato inteiro).
    /// </summary>
    public long BalanceAfter { get; set; }

    /// <summary>
    /// Gets or sets uma descrição livre (ex.: nome do item comprado, plugin de origem).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a data/hora em que a movimentação ocorreu.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
