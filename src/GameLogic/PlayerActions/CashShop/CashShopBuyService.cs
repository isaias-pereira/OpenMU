// <copyright file="CashShopBuyService.cs" company="MUnique">
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
/// Processes a cash shop purchase (C1 D2 03) by validating the catalog, debiting WCoinP and
/// delivering the item durably.
/// </summary>
/// <remarks>
/// <para>
/// The client sends only <c>(PackageProductSeq, ProductDisplaySeq, PriceSeq, ItemCode, CashType, MileageFlag)</c>,
/// so every value is re-validated against the server catalog before any coin is debited.
/// The <see cref="PurchaseErrorCode"/> values are dictated by <c>ReceiveIGS_BuyItem</c> in the client.
/// </para>
/// <para>
/// <b>Delivery target.</b> The purchased item goes to the cash shop item storage of the
/// <i>account</i> (<see cref="CashShopStorageItem"/>), not to the character inventory. After a
/// successful purchase the client re-requests the storage list (<c>D2 05</c>) and renders
/// <c>D2 0D</c>; an item placed in the character inventory would be invisible there and
/// <c>NoDeliverySpace</c> (code 2) would be reported for the wrong reason.
/// </para>
/// <para>
/// <b>Atomicity.</b> The storage row, the delivered <see cref="Item"/> and the WCOIN debit
/// are all staged on the player's <see cref="MUnique.OpenMU.Persistence.IContext"/> and written
/// by a single <c>SaveChangesAsync</c>, which the EF Core unit of work wraps in one
/// database transaction. Nothing is charged unless the delivery is committed with it.
/// The storage slot is not read from a separate context beforehand but reserved
/// (<see cref="ICashShopStorageService.ReserveNextStorageIndexAsync"/>) inside that same
/// context, so the slot, the capacity check and the debit are all resolved by one commit.
/// </para>
/// <para>
/// <b>Failed persistence.</b> <see cref="MUnique.OpenMU.Persistence.IContext"/> exposes no
/// transaction, rollback or detached unit of work, so a save that throws or returns
/// <c>false</c> leaves the staged changes tracked. <see cref="DiscardStagedPurchaseAsync"/>
/// therefore reverts the <i>complete</i> unit of work — the storage row, the item, the WCOIN
/// statement and the wallet balance — instead of only the delivery rows, so a later save of
/// the same context cannot persist a debit without its delivery. A rollback that cannot be
/// completed is logged as an error rather than swallowed.
/// </para>
/// <para>
/// <b>Concurrency.</b> Purchases of one account are serialised by the per-account
/// <see cref="AsyncLock"/> of this singleton service, which covers a single server
/// <i>process</i> only. The reservation in the player's own context additionally prevents two
/// purchases of the same account from taking the same slot, and the unique index
/// <c>(AccountId, StorageIndex)</c> rejects the delivery outright if another process won the
/// slot first — in that case the whole purchase fails and the player is not charged. Two
/// separate processes debiting the same wallet concurrently are not otherwise covered; see the
/// remarks of <see cref="WCoinService.TryDebit"/>.
/// </para>
/// </remarks>
public sealed class CashShopBuyService : ICashShopBuyService
{
    private readonly ICashShopSelectionResolver _selectionResolver;
    private readonly ICashShopStorageService _storageService;
    private readonly ILogger<CashShopBuyService> _logger;
    private readonly UnsafeContextRetirement _retirement;
    private readonly ConcurrentDictionary<Guid, AsyncLock> _purchaseLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopBuyService"/> class.
    /// </summary>
    /// <param name="selectionResolver">The shared catalog selection resolver.</param>
    /// <param name="storageService">The cash shop item storage service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="retirement">Retires sessions whose context could not be fully rolled back.</param>
    public CashShopBuyService(
        ICashShopSelectionResolver selectionResolver,
        ICashShopStorageService storageService,
        ILogger<CashShopBuyService> logger,
        UnsafeContextRetirement retirement)
    {
        this._selectionResolver = selectionResolver ?? throw new ArgumentNullException(nameof(selectionResolver));
        this._storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
    }

    /// <inheritdoc />
    public async ValueTask<PurchaseResult> ProcessPurchaseAsync(Player player, int packageProductSeq, int productDisplaySeq, int priceSeq, ushort itemCode, int coinType, bool mileageFlag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.SelectedCharacter is null || player.Account is not { } account)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "The player is not ready to buy an item.");
        }

        if (mileageFlag)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.CannotBeBought, "Mileage purchases are not supported.");
        }

        // Serialise per account so two concurrent requests cannot both pass the balance check.
        using var purchaseLock = await this._purchaseLocks.GetOrAdd(account.Id, _ => new AsyncLock()).LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The catalog rules live in the resolver, shared with the gift flow (D2 04), so both
            // flows accept and reject exactly the same selections.
            var selection = await this._selectionResolver.ResolveAsync(
                    player,
                    packageProductSeq,
                    productDisplaySeq,
                    priceSeq,
                    itemCode,
                    coinType,
                    mileageFlag,
                    requireGiftable: false,
                    cancellationToken).ConfigureAwait(false);
            if (!selection.Success)
            {
                return PurchaseResult.Failed(selection.ErrorCode, selection.ErrorMessage ?? "The selection could not be resolved.");
            }

            var price = selection.Price;
            var itemDefinition = selection.ItemDefinition!;
            var selectedPriceSequence = selection.SelectedPriceSequence;

            // The item is delivered to the cash shop storage, so the storage capacity — not the
            // character inventory — decides delivery failure (client code 2).
            //
            // The slot is reserved inside the player's own context, not read from a separate one:
            // the reservation then shares the single unit of work with the debit, so a slot that
            // this context already holds (a purchase still in flight) can never be handed out
            // twice. ReserveNextStorageIndexAsync also reports "full" by returning -1, so the
            // capacity check and the index allocation cannot disagree the way a separate
            // count-then-index read could.
            var reservation = await this._storageService
                .ReserveNextStorageIndexAsync(player.PersistenceContext, account.Id, forGift: false, cancellationToken)
                .ConfigureAwait(false);
            if (reservation.StorageItem is null)
            {
                return PurchaseResult.Failed(PurchaseErrorCode.NoDeliverySpace, "The cash shop storage of the account is full.");
            }

            var storageItem = reservation.StorageItem;
            var storageIndex = reservation.StorageIndex;

            // From here on the context holds staged state. Every exit path must undo it, and
            // any failure while staging it must undo whatever was already staged — otherwise a
            // later save of the player's context would persist a half-written purchase.
            Item? item = null;
            WCoinDebit? debit = null;
            try
            {
                item = player.PersistenceContext.CreateNew<Item>();
                item.Definition = itemDefinition;
                item.Durability = item.IsStackable() ? 1 : itemDefinition.Durability;

                storageItem.ItemSeq = storageIndex;
                storageItem.StorageGroup = selection.StorageGroup;
                storageItem.ProductSeq = selection.ProductSequence;
                storageItem.PriceSeq = selectedPriceSequence;
                storageItem.CashPoint = price;
                storageItem.ItemType = CashShopStorageItemType.Product;
                storageItem.Item = item;

                debit = await WCoinService.TryDebit(
                        player.PersistenceContext,
                        account.Id,
                        price,
                        WCoinTransactionType.Purchase,
                        $"CashShop Purchase: Package {packageProductSeq}, Price {selectedPriceSequence} ({selection.ProductName})").ConfigureAwait(false);
                if (debit is null)
                {
                    // Undo the staged rows so nothing dirty is left behind for a later save.
                    return await this.DiscardAndReportAsync(
                        player, storageItem, item, null, "Insufficient WCoinP balance.", PurchaseErrorCode.InsufficientBalance).ConfigureAwait(false);
                }

                // One SaveChangesAsync commits the delivery, the reservation and the debit in a
                // single database transaction, so the player is never charged for an item that
                // was not persisted.
                if (!await player.PersistenceContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    this._logger.LogError("The cash shop purchase was not committed for {Player}.", player.Name);
                    return await this.DiscardAndReportAsync(player, storageItem, item, debit, "The cash shop purchase could not be completed.").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The commit was cancelled. The staged state is still in the context, so it has to
                // be undone before the cancellation propagates: otherwise a later save of this
                // context would commit a purchase the client already abandoned. Cancellation is
                // deliberately rethrown rather than reported as TransactionFailed, so the caller
                // can tell "the server is going away" from "the purchase failed".
                var discarded = await this.DiscardStagedPurchaseAsync(player, storageItem, item, debit).ConfigureAwait(false);
                if (!discarded)
                {
                    // The rollback could not fully undo the staged state, so the context is
                    // potentially dirty. IContext has no "discard pending changes" API, so the
                    // session is retired: while the player stays in the game, the next session save
                    // would persist the leftover debit and charge him for a purchase he never got.
                    if (!await this._retirement.RetireAsync(player, "cancelled cash shop purchase with incomplete rollback").ConfigureAwait(false))
                    {
                        this._logger.LogCritical("Session retirement failed after a cancelled cash shop purchase for {Player}; manual inspection is required.", player.Name);
                    }
                }

                throw;
            }
            catch (Exception exception)
            {
                // The rollback runs here, in the handler body, and not in an exception filter:
                // a filter must decide synchronously, which would force the async rollback to be
                // blocked on with GetAwaiter().GetResult(). That is deadlock-prone as soon as any
                // step of the rollback becomes genuinely asynchronous.
                this._logger.LogError(exception, "The cash shop purchase could not be committed for {Player}.", player.Name);
                return await this.DiscardAndReportAsync(player, storageItem, item, debit, "The cash shop purchase could not be completed.").ConfigureAwait(false);
            }

            return PurchaseResult.Successful(selection.ProductName ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this._logger.LogError(exception, "Error processing cash shop purchase for {Player}.", player.Name);
            return PurchaseResult.Failed(PurchaseErrorCode.TransactionFailed, "The cash shop purchase could not be completed.");
        }
    }

    /// <summary>
    /// Undoes everything a purchase staged on the player's context, so a failed purchase can
    /// never be persisted later on as a debit without its delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole unit of work is undone: the storage row, the delivered <see cref="Item"/>, the
    /// staged WCOIN statement and the wallet mutation. Only the entities this purchase touched
    /// are reverted — <see cref="IContext.Detach"/> is never called on anything else, so
    /// legitimate pending changes of the player are preserved.
    /// </para>
    /// <para>
    /// The wallet is restored to the values it had before <see cref="WCoinService.TryDebit"/>
    /// and the statement is detached, which leaves the context exactly as it was before the
    /// purchase. Reverting the wallet and the statement together is what makes the guarantee
    /// hold: a later save of the player context cannot write a debit or a statement for a
    /// purchase that was never delivered.
    /// </para>
    /// <para>
    /// <b>No silent failures.</b> Each step is undone independently and every failure is logged
    /// with <c>Error</c> (not <c>Warning</c>) together with what could not be undone, because a
    /// partial rollback leaves the context able to charge the player for a purchase that was
    /// never delivered — the worst outcome of this flow. The result is reported to the caller so
    /// it can decide what to do instead of assuming the context is clean.
    /// </para>
    /// </remarks>
    /// <param name="player">The player whose context holds the staged purchase.</param>
    /// <param name="storageItem">The staged storage row.</param>
    /// <param name="item">The staged item, if it was created.</param>
    /// <param name="debit">The staged WCOIN debit, or <c>null</c> if the debit was refused for lack of balance.</param>
    /// <param name="message">The message reported when the rollback fully restored the context.</param>
    /// <param name="cleanRollbackError">The error to report when rollback succeeds.</param>
    /// <returns>
    /// <paramref name="cleanRollbackError"/> when the context was fully restored, or
    /// <see cref="PurchaseErrorCode.RollbackIncomplete"/> when session retirement was attempted
    /// because the context may still hold staged state.
    /// </returns>

    private async ValueTask<PurchaseResult> DiscardAndReportAsync(
        Player player,
        CashShopStorageItem storageItem,
        Item? item,
        WCoinDebit? debit,
        string message,
        PurchaseErrorCode cleanRollbackError = PurchaseErrorCode.TransactionFailed)
    {
        var discarded = await this.DiscardStagedPurchaseAsync(player, storageItem, item, debit).ConfigureAwait(false);
        if (!discarded)
        {
            // The rollback left the context possibly dirty, so the session is retired before the
            // result is returned: while the player stays in the game, the next session save would
            // persist the leftover debit or delivery row. The caller still gets a failure, never a
            // success, and the outcome is reported as RollbackIncomplete so it can be told apart
            // from a clean transaction failure.
            var retired = await this._retirement.RetireAsync(player, "incomplete cash shop purchase rollback").ConfigureAwait(false);
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
    /// <param name="player">The player whose context holds the staged purchase.</param>
    /// <param name="storageItem">The staged storage row.</param>
    /// <param name="item">The staged item.</param>
    /// <param name="debit">The staged debit, if any.</param>
    /// <returns>Whether every rollback step completed without throwing.</returns>
    private async ValueTask<bool> DiscardStagedPurchaseAsync(
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

        // Detach the storage row and the item separately. IContext.Detach recurses into the
        // aggregate members, so detaching the storage row already detaches its Item; doing both
        // keeps the guarantee if a future context implementation stops recursing, and neither
        // call touches an entity outside this purchase.
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
                "Could not fully discard the staged cash shop purchase for {Player}: the player context may still hold a pending debit or delivery row. {FailureCount} step(s) failed; subsequent SaveChangesAsync calls on this context must be treated as unsafe.",
                player.Name,
                failures.Count);
            return false;
        }

        return true;
    }
}
