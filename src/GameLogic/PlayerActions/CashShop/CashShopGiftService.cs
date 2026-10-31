// <copyright file="CashShopGiftService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using Nito.AsyncEx;

/// <summary>
/// Processes a cash shop gift request (C1 D2 04): buys a catalog product with the sender's
/// WCoinP and delivers it to the <i>receiver's</i> account storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery target.</b> The gift lands in the storage of the <b>receiver's account</b>, not the
/// sender's, and is flagged with <see cref="CashShopStorageItem.IsGift"/> so it shows up in the
/// client's 'G' (gift box) tab instead of mixing with the account's own purchases in 'S'. The
/// entity already lives in the account schema, so this needed one boolean column — see the
/// <c>AddCashShopStorageGiftFlag</c> migration — and no new table.
/// </para>
/// <para>
/// <b>Why IsGift and not a new ItemType.</b> The client rejects a <c>chItemType</c> other than
/// 'P' or 'C': <c>AddStorageItem</c> drops the line. A gift is still a product, so it keeps 'P'
/// and the tab is resolved by the server from <see cref="CashShopStorageItem.IsGift"/>.
/// </para>
/// <para>
/// <b>Atomicity.</b> The debit, the delivered <see cref="Item"/> and the storage row are staged
/// on the sender's <see cref="MUnique.OpenMU.Persistence.IContext"/> and written by a single
/// <c>SaveChangesAsync</c>, exactly as the buy flow does. The sender is never charged for a gift
/// that was not delivered, and a failure undoes the whole unit of work.
/// </para>
/// <para>
/// <b>Currency.</b> Only WCoinP is charged, because that is the only wallet this server has.
/// Other <c>CashType</c> values are refused by <see cref="ICashShopSelectionResolver"/>, which
/// the gift flow shares with the buy flow.
/// </para>
/// <para>
/// <b>Gift sender and message (Fase 10).</b> The client sends <c>GiftText</c> (200 bytes,
/// D2 04 offset 34) and this flow persists it with the <b>sender's authenticated character
/// name</b> on the receiver's storage row, in the same commit as the delivery. The D2 04
/// request carries no sender field, so the sender can never be spoofed from packet content.
/// The message is normalized for safe transmission only (<see cref="NormalizeGiftMessage"/>:
/// cut at the first NUL, empty/whitespace-only becomes null, capped at the column limit) and
/// is never written to the logs. The receiver's client reads both fields back on the gift-box
/// tab through the D2 0E packet (<c>PMSG_CASHSHOP_GIFTSTORAGELIST</c>), rendered as
/// "It's a gift from %s" plus the message box.
/// </para>
/// </remarks>
public sealed class CashShopGiftService : ICashShopGiftService
{
    /// <summary>
    /// Limite de <b>bytes UTF-8 úteis</b> da mensagem no campo de 200 bytes do D2 0E
    /// (199 bytes + NUL obrigatório dentro do campo). A persistência usa o MESMO limite em
    /// bytes — e não um limite de caracteres — para que o texto gravado seja exatamente o
    /// que o destinatário verá na tela, sem divergência silenciosa entre banco e fio.
    /// </summary>
    private const int MaxGiftMessageBytes = 199;

    private readonly ICashShopSelectionResolver _selectionResolver;
    private readonly ICashShopStorageService _storageService;
    private readonly ILogger<CashShopGiftService> _logger;
    private readonly UnsafeContextRetirement _retirement;
    private readonly ConcurrentDictionary<Guid, AsyncLock> _giftLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopGiftService"/> class.
    /// </summary>
    /// <param name="selectionResolver">The shared catalog selection resolver.</param>
    /// <param name="storageService">The cash shop item storage service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="retirement">Retires sessions whose context could not be fully rolled back.</param>
    public CashShopGiftService(
        ICashShopSelectionResolver selectionResolver,
        ICashShopStorageService storageService,
        ILogger<CashShopGiftService> logger,
        UnsafeContextRetirement retirement)
    {
        this._selectionResolver = selectionResolver ?? throw new ArgumentNullException(nameof(selectionResolver));
        this._storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
    }

    /// <inheritdoc />
    public async ValueTask<PurchaseResult> ProcessGiftAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        string receiverName,
        string giftText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.SelectedCharacter is null || player.Account is not { } account)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "The player is not ready to send a gift.");
        }

        // The receiver name is fixed-width (11 bytes) and NUL-padded, so it arrives with
        // trailing NULs; a name that is empty after cleaning is not a receiver.
        var receiver = NormalizeReceiverName(receiverName);
        if (receiver.Length == 0)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "A gift requires a receiver name.");
        }

        if (player.SelectedCharacter.Name.Equals(receiver, StringComparison.Ordinal))
        {
            // Sending a gift to yourself would deliver to the sender's own storage, which the
            // client does not expect from the gift flow. The buy flow (D2 03) is the one meant
            // for one's own items.
            return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "A gift cannot be sent to your own character.");
        }

        // Serialise per sending account so two concurrent gifts cannot both pass the balance
        // check. Same scope as the buy flow: this process only.
        using var giftLock = await this._giftLocks.GetOrAdd(account.Id, _ => new AsyncLock()).LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var selection = await this._selectionResolver.ResolveAsync(
                    player,
                    packageProductSeq,
                    productDisplaySeq,
                    priceSeq,
                    itemCode,
                    coinType,
                    mileageFlag,
                    requireGiftable: true,
                    cancellationToken).ConfigureAwait(false);
            if (!selection.Success)
            {
                return PurchaseResult.Failed(selection.ErrorCode, selection.ErrorMessage ?? "The selection could not be resolved.");
            }

            // Resolve the receiver's account *before* staging anything. The client sends a
            // character name, so the receiving account is the one that owns that character.
            var receiverAccount = await player.PersistenceContext
                .GetAccountByCharacterNameAsync(receiver, cancellationToken)
                .ConfigureAwait(false);
            if (receiverAccount is null)
            {
                this._logger.LogInformation("Cash shop gift: character {Receiver} does not exist.", receiver);
                return PurchaseResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The gift receiver does not exist.");
            }

            if (receiverAccount.Id == account.Id)
            {
                // The character belongs to the sender's own account (an alt character). The gift
                // would land in the sender's own storage, so it is refused like the self-gift
                // above instead of silently becoming a self-purchase.
                return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "A gift cannot be sent to your own account.");
            }

            var price = selection.Price;
            var itemDefinition = selection.ItemDefinition!;
            var selectedPriceSequence = selection.SelectedPriceSequence;

            // The delivery is validated against the catalog before any state is staged, so an
            // offer that cannot be delivered correctly is refused without touching the sender's
            // wallet or the receiver's slot. The same rule as a purchase, shared from
            // <see cref="CashShopBuyService.ResolveDeliveredDurabilities"/>: the catalog quantity
            // becomes the stack count of a stackable item; a quantity above the stack capacity,
            // or above one on a non-stackable item, is refused.
            var quantity = selection.Quantity < 1 ? 1 : selection.Quantity;
            var durabilities = CashShopBuyService.ResolveDeliveredDurabilities(itemDefinition, quantity, this._logger);
            if (durabilities.Count == 0)
            {
                return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "The selected item cannot be delivered in the requested quantity.");
            }

            // The slot is reserved in the sender's context, so the reservation, the delivery and
            // the debit are all committed by the single SaveChangesAsync below. A gift that
            // cannot be stored is refused before the sender is charged.
            var reservation = await this._storageService
                .ReserveNextStorageIndexAsync(player.PersistenceContext, receiverAccount.Id, forGift: true, cancellationToken)
                .ConfigureAwait(false);
            if (reservation.StorageItem is null)
            {
                return PurchaseResult.Failed(PurchaseErrorCode.NoDeliverySpace, "The gift storage of the receiver is full.");
            }

            var storageItem = reservation.StorageItem;

            Item? item = null;
            WCoinDebit? debit = null;
            try
            {
                item = player.PersistenceContext.CreateNew<Item>();
                item.Definition = itemDefinition;
                item.Durability = durabilities[0];

                storageItem.StorageGroup = selection.StorageGroup;
                storageItem.ProductSeq = selection.ProductSequence;
                storageItem.PriceSeq = selectedPriceSequence;
                storageItem.CashPoint = price;
                storageItem.ItemType = CashShopStorageItemType.Product;
                storageItem.Item = item;

                // Fase 10: os metadados do presente seguem na MESMA linha e no MESMO commit do
                // débito e da entrega. O remetente é a identidade autenticada da sessão — o
                // D2 04 não carrega campo de remetente, então nada vindo do cliente pode
                // falsificá-lo. O rollback existente (DiscardStagedGiftAsync) descarta a linha
                // com os metadados junto; nenhuma gravação parcial é possível.
                storageItem.GiftSender = player.SelectedCharacter!.Name;
                storageItem.GiftMessage = NormalizeGiftMessage(giftText);

                // Fase 10 (expiração real): a duração contratada segue com o presente para a linha
                // do destinatário, congelada do catálogo no momento do envio — o destinatário
                // recebe exatamente o que foi comprado, mesmo que o catálogo mude depois. A
                // duração não ativa aqui; a contagem começa no claim do destinatário.
                storageItem.DurationSeconds = selection.DurationSeconds;

                debit = await WCoinService.TryDebit(
                        player.PersistenceContext,
                        account.Id,
                        price,
                        WCoinTransactionType.Purchase,
                        $"CashShop Gift: Package {packageProductSeq}, Price {selectedPriceSequence} ({selection.ProductName}) to {receiver}").ConfigureAwait(false);
                if (debit is null)
                {
                    return await this.DiscardAndReportAsync(
                        player, storageItem, item, null, "Insufficient WCoinP balance.", PurchaseErrorCode.InsufficientBalance).ConfigureAwait(false);
                }

                if (!await player.PersistenceContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    this._logger.LogError("The cash shop gift was not committed for {Player}.", player.Name);
                    return await this.DiscardAndReportAsync(player, storageItem, item, debit, "The cash shop gift could not be completed.").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Undo the staged state before the cancellation propagates, so a later save of
                // this context cannot commit a gift the client already abandoned. A cancelled
                // operation is never reported as a success: the cancellation is rethrown below.
                var discarded = await this.DiscardStagedGiftAsync(player, storageItem, item, debit).ConfigureAwait(false);
                if (!discarded)
                {
                    // The rollback could not fully undo the staged debit/delivery, so the context
                    // is potentially dirty. IContext has no "discard pending changes" API, so the
                    // session is retired: while the player stays in the game, the next session save
                    // would persist the leftover state and charge him for a gift he never got.
                    if (!await this._retirement.RetireAsync(player, "cancelled cash shop gift with incomplete rollback").ConfigureAwait(false))
                    {
                        this._logger.LogCritical("Session retirement failed after a cancelled cash shop gift for {Player}; manual inspection is required.", player.Name);
                    }
                }

                throw;
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "The cash shop gift could not be committed for {Player}.", player.Name);
                return await this.DiscardAndReportAsync(player, storageItem, item, debit, "The cash shop gift could not be completed.").ConfigureAwait(false);
            }

            this._logger.LogInformation(
                "Cash shop gift sent: {Sender} -> {Receiver}, product {ProductName} for {Price}.",
                player.Name,
                receiver,
                selection.ProductName,
                price);

            return PurchaseResult.Successful(selection.ProductName ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this._logger.LogError(exception, "Error processing cash shop gift for {Player}.", player.Name);
            return PurchaseResult.Failed(PurchaseErrorCode.TransactionFailed, "The cash shop gift could not be completed.");
        }
    }

    /// <summary>
    /// Cleans the fixed-width receiver name the client sent, removing the NUL padding.
    /// </summary>
    /// <param name="receiverName">The raw name from the packet.</param>
    /// <returns>The cleaned name, or an empty string when there is no receiver.</returns>
    private static string NormalizeReceiverName(string? receiverName)
    {
        if (string.IsNullOrWhiteSpace(receiverName))
        {
            return string.Empty;
        }

        // The field is a fixed 11-byte region, so it arrives NUL-padded; the client may also add
        // surrounding blanks. Neither belongs to the character name that is looked up.
        return receiverName!.Replace('\0', ' ').Trim();
    }

    /// <summary>
    /// Normaliza a mensagem do presente para gravação e transmissão segura no D2 0E.
    /// </summary>
    /// <param name="giftText">A mensagem como o cliente a enviou.</param>
    /// <returns>
    /// A mensagem a persistir, ou <c>null</c> quando não há conteúdo visível.
    /// </returns>
    /// <remarks>
    /// Política determinística, no mesmo espírito do que o cliente apresenta:
    /// <list type="bullet">
    /// <item><description>corta no primeiro NUL — o campo do fio e o cliente
    /// (<c>ExtractString</c> no servidor, <c>MultiByteToWideChar</c> no cliente) terminam nele,
    /// então nada além dele é conteúdo apresentável;</description></item>
    /// <item><description>texto vazio ou só de espaços vira <c>null</c> — presente sem
    /// mensagem, sem texto inventado;</description></item>
    /// <item><description>limita a <see cref="MaxGiftMessageBytes"/> <b>bytes UTF-8</b>, em
    /// fronteira de caractere — o MESMO limite do campo de 200 bytes do D2 0E. Assim o texto
    /// persistido é exatamente o texto que o destinatário verá: a coluna varchar(200) só
    /// precisa acomodar o que o fio carrega (chars ≤ bytes);</description></item>
    /// <item><description>acentos e demais caracteres UTF-8 válidos passam como foram
    /// enviados, sem transformação;</description></item>
    /// <item><description>o conteúdo nunca é escrito nos logs.</description></item>
    /// </list>
    /// </remarks>
    private static string? NormalizeGiftMessage(string? giftText)
    {
        if (string.IsNullOrEmpty(giftText))
        {
            return null;
        }

        var nulIndex = giftText.IndexOf('\0');
        var content = nulIndex >= 0 ? giftText[..nulIndex] : giftText;
        var truncated = TruncateToUtf8Bytes(content, MaxGiftMessageBytes);
        return truncated.Trim().Length == 0 ? null : truncated;
    }

    /// <summary>
    /// Trunca o texto em no máximo <paramref name="maxBytes"/> bytes UTF-8, sem dividir um
    /// caractere: o corte caminha de volta sobre continuation bytes até uma fronteira.
    /// Surrogates órfãos do .NET são substituídos por U+FFFD pelo encoder (saída sempre
    /// UTF-8 bem-formada). É o mesmo contrato binário do serializer D2 0E.
    /// </summary>
    private static string TruncateToUtf8Bytes(string content, int maxBytes)
    {
        var encoded = System.Text.Encoding.UTF8.GetBytes(content);
        if (encoded.Length <= maxBytes)
        {
            return content;
        }

        var useful = maxBytes;
        while (useful > 0 && (encoded[useful] & 0xC0) == 0x80)
        {
            useful--;
        }

        return System.Text.Encoding.UTF8.GetString(encoded, 0, useful);
    }

    /// <summary>
    /// Discards the staged gift and attempts session retirement when rollback is incomplete.
    /// </summary>
    /// <param name="player">The player whose context holds the staged gift.</param>
    /// <param name="storageItem">The staged storage row of the receiver.</param>
    /// <param name="item">The staged item, if it was created.</param>
    /// <param name="debit">The staged WCOIN debit, if one was prepared.</param>
    /// <param name="message">The message for a clean rollback.</param>
    /// <param name="cleanRollbackError">The error to report when rollback succeeds.</param>
    /// <returns>
    /// <paramref name="cleanRollbackError"/> when the context was fully restored, or
    /// <see cref="PurchaseErrorCode.RollbackIncomplete"/> when it may still hold staged state.
    /// </returns>
    private async ValueTask<PurchaseResult> DiscardAndReportAsync(
        Player player,
        CashShopStorageItem storageItem,
        Item? item,
        WCoinDebit? debit,
        string message,
        PurchaseErrorCode cleanRollbackError = PurchaseErrorCode.TransactionFailed)
    {
        var discarded = await this.DiscardStagedGiftAsync(player, storageItem, item, debit).ConfigureAwait(false);
        if (!discarded)
        {
            // The rollback left the context possibly dirty, so the session is retired before the
            // result is returned: while the player stays in the game, the next session save would
            // persist the leftover debit or delivery row. The caller still gets a failure, never
            // a success, and the outcome is reported as RollbackIncomplete so the caller can
            // tell it apart from a clean transaction failure.
            var retired = await this._retirement.RetireAsync(player, "incomplete cash shop gift rollback").ConfigureAwait(false);
            return PurchaseResult.Failed(
                PurchaseErrorCode.RollbackIncomplete,
                message + (retired
                    ? " The player session was retired without saving; manual inspection is required."
                    : " Session retirement could not be completed; manual inspection is required."));
        }

        return PurchaseResult.Failed(cleanRollbackError, message);
    }

    /// <summary>
    /// Attempts each rollback step, preserving failures for the caller.
    /// </summary>
    /// <param name="player">The sender whose context holds the staged gift.</param>
    /// <param name="storageItem">The staged storage row.</param>
    /// <param name="item">The staged item.</param>
    /// <param name="debit">The staged debit, if any.</param>
    /// <returns>Whether every rollback step completed without throwing.</returns>
    private async ValueTask<bool> DiscardStagedGiftAsync(
        Player player,
        CashShopStorageItem storageItem,
        Item? item,
        WCoinDebit? debit)
    {
        var failures = new List<Exception>();

        // The wallet and its statement first: undoing the delivery rows alone would leave a
        // debit in the context, which is what actually costs the player money.
        if (debit is not null)
        {
            try
            {
                await WCoinService.RevertDebitAsync(player.PersistenceContext, debit).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        // Detach the storage row and the item separately, so neither call can be skipped
        // because the other one failed. Neither touches an entity outside this gift.
        foreach (var entity in new object?[] { storageItem, item })
        {
            if (entity is null)
            {
                continue;
            }

            try
            {
                player.PersistenceContext.Detach(entity);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            this._logger.LogError(
                failures[0],
                "Could not fully discard the staged cash shop gift for {Player}: the player context may still hold a pending debit or delivery row. {FailureCount} step(s) failed; subsequent SaveChangesAsync calls on this context must be treated as unsafe.",
                player.Name,
                failures.Count);
            return false;
        }

        return true;
    }
}
