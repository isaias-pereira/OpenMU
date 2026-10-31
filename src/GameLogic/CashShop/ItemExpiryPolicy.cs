// <copyright file="ItemExpiryPolicy.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Política central de expiração de itens temporários da Cash Shop (Fase 10). Os timestamps
/// persistidos (<see cref="Item.ExpiresAtUtc"/>) são a única fonte de verdade: um item com
/// <c>ExpiresAtUtc</c> no passado está expirado, independentemente de quem o carrega ou de
/// quanto tempo o servidor ficou fora do ar.
/// </summary>
/// <remarks>
/// <para>
/// Estados (ver <c>docs/cashshop/item-expiry-contract.md</c>):
/// </para>
/// <list type="bullet">
/// <item><description><b>Legado/permanente</b>: <c>ExpiresAtUtc</c> <c>null</c> — nunca expira e
/// nunca recebe valor retroativo. Todos os caminhos de enforcement passam direto.</description></item>
/// <item><description><b>Temporário ativo</b>: <c>ExpiresAtUtc</c> no futuro — utilizável,
/// equipável, negociável; o prazo viaja com a instância em qualquer transferência.</description></item>
/// <item><description><b>Temporário expirado</b>: <c>nowUtc ≥ ExpiresAtUtc</c> — não utilizável,
/// não equipável, não negociável, não transferível e sem atributos; fica congelado onde está
/// até a limpeza (GATE G) o remover. Corrompido → fail-safe (tratado como expirado/bloqueado).</description></item>
/// </list>
/// <para>
/// <see cref="Item.ActivatedAtUtc"/> não participa do cálculo de expiração: o prazo absoluto
/// (<see cref="Item.ExpiresAtUtc"/>) já embute a duração contratada e é o que o servidor cumpre.
/// </para>
/// </remarks>
public static class ItemExpiryPolicy
{
    /// <summary>
    /// Determina se o item carrega prazo contratado (metadado de expiração presente).
    /// </summary>
    /// <param name="item">O item.</param>
    /// <returns><c>true</c> se o item é temporário (com prazo); <c>false</c> para legado/permanente.</returns>
    public static bool IsTemporary(this Item? item) => item?.ExpiresAtUtc is not null;

    /// <summary>
    /// Determina se o item está expirado: tem prazo contratado e o relógio UTC atual já o
    /// alcançou. Itens legados/permanentes (<c>null</c>) nunca estão expirados.
    /// </summary>
    /// <param name="item">O item.</param>
    /// <returns><c>true</c> se o item expirou; <c>false</c> para legado/permanente/ativo.</returns>
    public static bool IsExpired(this Item? item) =>
        item?.ExpiresAtUtc is { } expires && DateTime.UtcNow >= expires;
}
