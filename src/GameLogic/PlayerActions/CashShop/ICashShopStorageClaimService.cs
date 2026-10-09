// <copyright file="ICashShopStorageClaimService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Retirada de um item do storage da Cash Shop da conta para o inventário do personagem
/// (o fluxo de "usar" do cliente, <c>C1 D2 0B</c> — <c>STORAGE_ITEM_USE_REQ</c>).
/// </summary>
public interface ICashShopStorageClaimService
{
    /// <summary>
    /// Move o item de uma linha do storage da conta para o inventário do personagem selecionado
    /// e consome a linha, atomicamente.
    /// </summary>
    /// <param name="player">O jogador, cuja conta autenticada é a dona do storage.</param>
    /// <param name="storageIndex">O <c>lStorageSeq</c> enviado pelo cliente (o StorageIndex da linha).</param>
    /// <param name="itemSeq">O <c>lStorageItemSeq</c> enviado pelo cliente (o ItemSeq da linha).</param>
    /// <param name="storageItemType">O tipo de storage enviado pelo cliente ('P' produto / 'C' moeda).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O resultado da retirada, com o código que a resposta D2 0B carrega.</returns>
    ValueTask<StorageClaimResult> ClaimAsync(
        Player player,
        int storageIndex,
        int itemSeq,
        byte storageItemType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// O resultado de uma retirada do storage da Cash Shop.
/// </summary>
public sealed record StorageClaimResult
{
    /// <summary>
    /// Gets a value indicating whether the item was delivered to the inventory and the storage
    /// row was consumed in the same commit.
    /// </summary>
    public bool Success => this.ErrorCode == StorageClaimErrorCode.Success;

    /// <summary>
    /// Gets the result code carried by the D2 0B response. The values are dictated by
    /// <c>ReceiveIGS_UseStorageItem</c> in the client.
    /// </summary>
    public StorageClaimErrorCode ErrorCode { get; init; }

    /// <summary>Gets the message describing the failure.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful claim.</summary>
    public static StorageClaimResult Successful() => new() { ErrorCode = StorageClaimErrorCode.Success };

    /// <summary>Creates a failed claim.</summary>
    /// <param name="errorCode">The reason for the failure.</param>
    /// <param name="errorMessage">The message describing the failure.</param>
    public static StorageClaimResult Failed(StorageClaimErrorCode errorCode, string errorMessage) => new() { ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>
/// Os códigos de resultado da retirada, ditados por <c>ReceiveIGS_UseStorageItem</c> no cliente
/// (<c>WSclient.cpp:12716-12804</c>). O cliente compara <c>byResult</c> como BYTE contra
/// <c>static_cast<BYTE>(-1)</c> e <c>static_cast<BYTE>(-2)</c>, então os códigos negativos
/// saem em complemento de dois (255/254).
/// </summary>
public enum StorageClaimErrorCode
{
    /// <summary>Sucesso: o item foi entregue e a linha consumida; o cliente reenvia a lista do storage.</summary>
    Success = 0,

    /// <summary>Erro genérico ("ThereHasBeenAnError" no cliente).</summary>
    Error = -1,

    /// <summary>Erro de acesso ao banco ("ADatabaseAccessErrorHasOccurred" no cliente).</summary>
    DatabaseError = -2,

    /// <summary>A linha não existe para a conta autenticada ("use storage não existe").</summary>
    StorageNotFound = 1,

    /// <summary>O inventário do personagem não tem espaço para o item.</summary>
    InventoryFull = 21,

    /// <summary>A entrada do storage não pode ser usada (sem item entregue, sem definição ou tipo não suportado).</summary>
    CannotUse = 22,
}
