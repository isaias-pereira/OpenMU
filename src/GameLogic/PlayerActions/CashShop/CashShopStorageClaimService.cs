// <copyright file="CashShopStorageClaimService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.Persistence;
using Nito.AsyncEx;

/// <summary>
/// Processes a cash shop storage claim (C1 D2 0B, <c>STORAGE_ITEM_USE_REQ</c>): moves the item of
/// one storage row to the character inventory and consumes the row, atomically.
/// </summary>
/// <remarks>
/// <para>
/// <b>Authority.</b> The client sends only <c>(StorageSeq, StorageItemSeq, ItemCode, StorageType)</c>
/// — the <c>StorageSeq</c>/<c>StorageItemSeq</c> pair (the fields the OpenMU structs misleadingly
/// name <c>BaseItemCode</c>/<c>MainItemCode</c>) and an item code derived from the client's local
/// catalog, which is <b>never</b> the authority. The server resolves the row against the
/// authenticated account's persisted rows; the account is always the connected player's own, never
/// a value from the client.
/// </para>
/// <para>
/// <b>Identification.</b> The client identifies the row by the <c>(StorageIndex, ItemSeq)</c> pair
/// it received in the D2 0D line. The D2 0B request does not carry the storage tab, and the
/// pair would be ambiguous across the two tabs ('S' purchases and 'G' gifts can hold the same
/// index). The reservation therefore allocates <c>ItemSeq</c> as a sequence that is unique per
/// account across both tabs, so the pair resolves to exactly one row.
/// </para>
/// <para>
/// <b>Validation before consumption.</b> The row is removed only after every check passed: the
/// line exists and belongs to the account, the delivered item and its definition exist, the item
/// type is supported, and the inventory has space for it. An entry whose item has no definition
/// (the accepted item 6702 incompatibility, for example) keeps its storage row and delivers
/// nothing — the row is never removed before the delivery is certain.
/// </para>
/// <para>
/// <b>Item resolution.</b> A row committed by another session (a gift received while this
/// character was already logged in, a purchase from another process) is not visible to the
/// player's context as an item: the persistence layer resolves the row's <c>Item</c> navigation
/// through the configuration cache, which only holds configuration items (merchant store stock),
/// never account items. The claim therefore falls back to a short-lived full context to load the
/// row's item (with its definition) from the database, and takes ownership of the item in the
/// player's context before staging — the account context ignores configuration types, so the
/// ownership transfer drags no configuration graph along, and the delivered item keeps its
/// persistent identity.
/// </para>
/// <para>
/// <b>Atomicity.</b> The item move and the row removal are staged on the player's context and
/// written by a single <c>SaveChangesAsync</c>, which the EF Core unit of work wraps in one
/// database transaction. Either the item stays in the storage (failed claim) or it is delivered
/// to the inventory (successful claim) — never both, never neither. The referenced item survives
/// the row removal: the row's item reference is cleared before the removal (the provider's
/// <c>DeleteAsync</c> would otherwise remove the row's <c>[MemberOfAggregate]</c> members — the
/// delivered item itself), and the database cascade is Item → row (deleting the <i>item</i>
/// removes the row; deleting the row never touches the item), verified against the homologation
/// database.
/// </para>
/// <para>
/// <b>Concurrency.</b> Claims of one account are serialised by the per-account
/// <see cref="AsyncLock"/> of this singleton service (one process only). A second claim of the
/// same row finds no row — the first claim consumed it in the same commit that delivered the
/// item — so one storage row yields at most one delivered item. Failed persistence reverts the
/// complete unit of work (the row stays, the item stays in the storage); a rollback that cannot
/// be completed retires the session, as the buy and gift flows do.
/// </para>
/// <para>
/// <b>No second charge.</b> The claim debits nothing: the purchase or gift that created the row
/// was charged when it was created.
/// </para>
/// </remarks>
public sealed class CashShopStorageClaimService : ICashShopStorageClaimService
{
    private readonly ILogger<CashShopStorageClaimService> _logger;
    private readonly UnsafeContextRetirement _retirement;
    private readonly Func<IContext> _itemContextFactory;
    private readonly Func<DateTime> _utcNowProvider;
    private readonly ConcurrentDictionary<Guid, AsyncLock> _claimLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopStorageClaimService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="retirement">Retires sessions whose context could not be fully rolled back.</param>
    /// <param name="itemContextFactory">
    /// Creates the short-lived context which resolves a storage row's item when the player's
    /// context cannot (rows committed by other sessions — see the item resolution remarks).
    /// </param>
    /// <param name="utcNowProvider">
    /// The UTC clock used to activate a contracted duration at claim time. Injectable for tests;
    /// production uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    public CashShopStorageClaimService(ILogger<CashShopStorageClaimService> logger, UnsafeContextRetirement retirement, Func<IContext> itemContextFactory, Func<DateTime>? utcNowProvider = null)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
        this._itemContextFactory = itemContextFactory ?? throw new ArgumentNullException(nameof(itemContextFactory));
        this._utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async ValueTask<StorageClaimResult> ClaimAsync(
        Player player,
        int storageIndex,
        int itemSeq,
        byte storageItemType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.SelectedCharacter is null || player.Account is not { } account || player.Inventory is null)
        {
            return StorageClaimResult.Failed(StorageClaimErrorCode.Error, "The player is not ready to claim a storage item.");
        }

        // Serialise per account so two concurrent claims of one account cannot both resolve the
        // same row. Same scope as the buy and gift flows: this process only.
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
        this._logger.LogInformation(
            "[claim-trace {CorrelationId}] claim STARTED for {Player}: storage={StorageIndex}, itemSeq={ItemSeq}.",
            correlationId,
            player.Name,
            storageIndex,
            itemSeq);
        var lockStopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var claimLock = await this._claimLocks.GetOrAdd(account.Id, _ => new AsyncLock()).LockAsync(cancellationToken).ConfigureAwait(false);
        lockStopwatch.Stop();
        this._logger.LogInformation(
            "[claim-trace {CorrelationId}] step lockWait in {ElapsedMs} ms.",
            correlationId,
            lockStopwatch.ElapsedMilliseconds);
        try
        {
            // The client's item code is never the authority: the row is resolved against the
            // authenticated account's persisted rows. The item code sent by the client is only
            // logged by the handler for diagnostics.
            this._logger.LogDebug(
                "Cash shop claim request: account {AccountId}, StorageIndex {StorageIndex}, ItemSeq {ItemSeq}, storage type {StorageType}.",
                account.Id,
                storageIndex,
                itemSeq,
                storageItemType);

            bool MatchesRow(CashShopStorageItem row) =>
                row.AccountId == account.Id
                && row.ItemSeq == itemSeq
                && row.StorageIndex == storageIndex
                && IsSupportedItemType(row.ItemType);

            var rowsStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var rows = await player.PersistenceContext.GetAsync<CashShopStorageItem>(cancellationToken).ConfigureAwait(false);
            var rowCount = rows is ICollection<CashShopStorageItem> rc ? rc.Count : -1;
            rowsStopwatch.Stop();
            this._logger.LogInformation(
                "[claim-trace {CorrelationId}] step playerContextRows in {ElapsedMs} ms (rows={RowCount}).",
                correlationId,
                rowsStopwatch.ElapsedMilliseconds,
                rowCount);
            var storageItem = rows.FirstOrDefault(MatchesRow);
            if (storageItem is null)
            {
                // The row does not exist for this account (or belongs to another account, which
                // is indistinguishable here by design: nothing about another account's rows is
                // revealed). Nothing is staged, nothing is removed.
                this._logger.LogInformation(
                    "Cash shop claim: account {AccountId} has no matching storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}).",
                    account.Id,
                    storageIndex,
                    itemSeq);
                return StorageClaimResult.Failed(StorageClaimErrorCode.StorageNotFound, "The storage entry does not exist.");
            }

            Item item;
            IContext? itemContext = null;
            try
            {
                if (storageItem.Item is { } trackedItem)
                {
                    // The row was created within this player's context (the purchase of this
                    // session): the item navigation is resolved and the item is already tracked.
                    item = trackedItem;
                }
                else
                {
                    // The row was committed by another session or process. The player's context
                    // cannot resolve the item navigation of such rows (the persistence layer
                    // routes item loads through the configuration cache, which holds no account
                    // items), so the row would look item-less here. Load exactly this row through
                    // a short-lived full context instead, by its primary key (the Id was read by
                    // the player's context above) — a point query which materializes only this
                    // row and its declared item graph. The previous implementation called
                    // GetAsync<CashShopStorageItem>, which loads the WHOLE table (measured ~30 s
                    // per claim on the homologation database — rodadas 2AM/2AR/2AS).
                    var fallbackStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    itemContext = this._itemContextFactory();
                    var contextCreated = fallbackStopwatch.ElapsedMilliseconds;
                    var deliveredRow = await itemContext.GetByIdAsync<CashShopStorageItem>(storageItem.Id, cancellationToken).ConfigureAwait(false);
                    var fallbackStop = fallbackStopwatch.ElapsedMilliseconds;
                    this._logger.LogInformation(
                        "[claim-trace {CorrelationId}] step fallbackContext in {ContextMs} ms, fallbackGetById in {TotalMs} ms (point query by primary key).",
                        correlationId,
                        contextCreated,
                        fallbackStop);
                    var deliveredItem = deliveredRow is not null && MatchesRow(deliveredRow)
                        ? deliveredRow.Item
                        : null;
                    if (deliveredItem is null)
                    {
                        this._logger.LogWarning(
                            "Cash shop claim: the storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}) of account {AccountId} has no delivered item.",
                            storageIndex,
                            itemSeq,
                            account.Id);
                        return StorageClaimResult.Failed(StorageClaimErrorCode.CannotUse, "The storage entry has no item to claim.");
                    }

                    item = deliveredItem;
                }
            }
            finally
            {
                itemContext?.Dispose();
            }

            if (item.Definition is null)
            {
                // The accepted item 6702 incompatibility shape: the row's item has no definition.
                // Nothing is delivered, nothing is removed, no exception escapes: the row stays.
                this._logger.LogWarning(
                    "Cash shop claim: the storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}) of account {AccountId} has an item without a definition; the claim was refused.",
                    storageIndex,
                    itemSeq,
                    account.Id);
                return StorageClaimResult.Failed(StorageClaimErrorCode.CannotUse, "The storage entry's item is not available.");
            }

            if (storageItem.DurationSeconds < 0)
            {
                // Fase 10 (expiração real): a duração contratada nunca pode ser negativa. Uma linha
                // assim é metadado corrompido — ativar um prazo dela produziria um item com
                // expiração sem sentido, e entregá-la como permanente mascararia a corrupção.
                // Fail-safe: o claim é recusado e a linha permanece no storage, nada é entregue.
                this._logger.LogError(
                    "Cash shop claim: the storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}) of account {AccountId} has an invalid contracted duration ({DurationSeconds}); the claim was refused.",
                    storageIndex,
                    itemSeq,
                    account.Id,
                    storageItem.DurationSeconds);
                return StorageClaimResult.Failed(StorageClaimErrorCode.CannotUse, "The storage entry's duration metadata is invalid.");
            }

            // The inventory space is validated before anything is staged: a full inventory
            // leaves the row in the storage and delivers nothing (client code 21).
            var spaceStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var toSlot = player.Inventory.CheckInvSpace(item);
            spaceStopwatch.Stop();
            this._logger.LogInformation(
                "[claim-trace {CorrelationId}] step inventorySpace in {ElapsedMs} ms (definition={DefinitionName}).",
                correlationId,
                spaceStopwatch.ElapsedMilliseconds,
                item.Definition.Name);
            if (toSlot is null)
            {
                this._logger.LogInformation(
                    "Cash shop claim: the inventory of {Player} is full; the storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}) stays.",
                    player.Name,
                    storageIndex,
                    itemSeq);
                return StorageClaimResult.Failed(StorageClaimErrorCode.InventoryFull, "The character inventory is full.");
            }

            // From here on the context holds staged state. Every exit path must undo it, so a
            // failed claim can never be persisted later on as a removal without its delivery.
            var previousSlot = item.ItemSlot;
            try
            {
                // Take ownership of the item in the player's context. For a row of this session
                // the item is already tracked (no-op); for a row loaded through the fallback
                // context the item is detached, and the account context ignores configuration
                // types, so attaching it drags no configuration graph along. From here the item
                // participates in the single committing SaveChangesAsync below.
                player.PersistenceContext.Attach(item);

                // The item moves to the inventory: the slot and the inventory storage of the
                // character. AddItemAsync updates the in-memory inventory and the character's
                // item storage (the EF navigation sets the item's ItemStorageId on the save).
                item.ItemSlot = (byte)toSlot;
                if (!await player.Inventory.AddItemAsync((byte)toSlot, item).ConfigureAwait(false))
                {
                    item.ItemSlot = previousSlot;
                    return StorageClaimResult.Failed(StorageClaimErrorCode.InventoryFull, "The character inventory is full.");
                }

                // Fase 10 (expiração real): a contagem começa EXATAMENTE no commit que entrega o
                // item ao inventário — nunca na compra, no recebimento do presente, na listagem,
                // no login ou ao equipar. Ativação e prazo são gravados no MESMO SaveChangesAsync
                // que move o item e consome a linha: ou o item é entregue com os timestamps, ou
                // nada é gravado. O relógio é UTC absoluto e persistido: a expiração continua
                // valendo através de logout, restart e recriação do container. A ativação é
                // exatamente única: um item já ativado (por exemplo por um caminho alternativo)
                // mantém a ativação original — nada renova nem prorroga o prazo; o prazo é
                // recalculado a partir da ativação preservada e da duração contratada da linha.
                if (storageItem.DurationSeconds > 0)
                {
                    item.ActivatedAtUtc ??= this._utcNowProvider();
                    item.ExpiresAtUtc = item.ActivatedAtUtc!.Value + TimeSpan.FromSeconds(storageItem.DurationSeconds.Value);
                }
                else
                {
                    // Permanente (0 = explícito, NULL = legado): nenhum timestamp é gravado,
                    // e nenhum timestamp pré-existente é inventado retroativamente.
                }

                // The row is removed in the same context, the referenced item survives it. The
                // EF provider's DeleteAsync also removes the row's [MemberOfAggregate] members —
                // which is the delivered item itself! — so the item reference is cleared first:
                // only the row is removed, and the item (now owned by the character inventory)
                // is written by the same commit. The database cascade is Item → row; deleting
                // the row never touches the item on the database level.
                storageItem.Item = null;
                await player.PersistenceContext.DeleteAsync(storageItem).ConfigureAwait(false);

                // One SaveChangesAsync commits the item move and the row removal in a single
                // database transaction, so the item is never lost or duplicated.
                var saveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (!await player.PersistenceContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    saveStopwatch.Stop();
                    this._logger.LogError(
                        "[claim-trace {CorrelationId}] SaveChangesAsync FAILED after {ElapsedMs} ms for {Player}.",
                        correlationId,
                        saveStopwatch.ElapsedMilliseconds,
                        player.Name);
                    this._logger.LogError("The cash shop claim was not committed for {Player}.", player.Name);
                    await this.RollbackClaimAsync(player, storageItem, item, previousSlot).ConfigureAwait(false);
                    return StorageClaimResult.Failed(StorageClaimErrorCode.DatabaseError, "The cash shop claim could not be completed.");
                }

                saveStopwatch.Stop();
                this._logger.LogInformation(
                    "[claim-trace {CorrelationId}] SaveChangesAsync FINISHED in {ElapsedMs} ms for {Player}.",
                    correlationId,
                    saveStopwatch.ElapsedMilliseconds,
                    player.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The commit was cancelled. The staged state is still in the context, so it has
                // to be undone before the cancellation propagates. Cancellation is rethrown
                // rather than reported as a claim failure, so the caller can tell "the server is
                // going away" from "the claim failed".
                var discarded = await this.RollbackClaimAsync(player, storageItem, item, previousSlot).ConfigureAwait(false);
                if (!discarded)
                {
                    if (!await this._retirement.RetireAsync(player, "cancelled cash shop claim with incomplete rollback").ConfigureAwait(false))
                    {
                        this._logger.LogCritical("Session retirement failed after a cancelled cash shop claim for {Player}; manual inspection is required.", player.Name);
                    }
                }

                throw;
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "The cash shop claim could not be committed for {Player}.", player.Name);
                await this.RollbackClaimAsync(player, storageItem, item, previousSlot).ConfigureAwait(false);
                return StorageClaimResult.Failed(StorageClaimErrorCode.Error, "The cash shop claim could not be completed.");
            }

            overallStopwatch.Stop();
            this._logger.LogInformation(
                "[claim-trace {CorrelationId}] claim FINISHED in {ElapsedMs} ms for {Player}.",
                correlationId,
                overallStopwatch.ElapsedMilliseconds,
                player.Name);
            this._logger.LogInformation(
                "Cash shop claim: {Player} claimed the storage row (StorageIndex {StorageIndex}, ItemSeq {ItemSeq}), item {ItemName} to inventory slot {Slot}.",
                player.Name,
                storageIndex,
                itemSeq,
                item.Definition.Name,
                toSlot);

            await player.InvokeViewPlugInAsync<IUpdateInventoryListPlugIn>(p => p.UpdateInventoryListAsync()).ConfigureAwait(false);
            return StorageClaimResult.Successful();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this._logger.LogError(exception, "Error processing cash shop claim for {Player}.", player.Name);
            return StorageClaimResult.Failed(StorageClaimErrorCode.Error, "The cash shop claim could not be completed.");
        }
    }

    /// <summary>
    /// Determines whether a storage row's item type is supported by this flow. Only product rows
    /// ('P') are claimable; currency rows ('C') are not implemented.
    /// </summary>
    private static bool IsSupportedItemType(string? itemType) =>
        string.Equals(itemType, CashShopStorageItemType.Product, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts each rollback step, preserving failures for the caller: the storage row stays,
    /// the item stays in the storage, and the in-memory inventory is reverted.
    /// </summary>
    private async ValueTask<bool> RollbackClaimAsync(Player player, CashShopStorageItem row, Item item, byte previousSlot)
    {
        var failures = new List<Exception>();

        // The in-memory inventory first: the item must leave the inventory view and return to
        // the storage semantics, so the player never sees an item that the database still keeps
        // in the cash shop storage. player.Inventory was validated non-null before staging.
        try
        {
            await player.Inventory!.RemoveItemAsync(item).ConfigureAwait(false);
            item.ItemSlot = previousSlot;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        // Then the context: detach the row (abandons the staged removal) and the item (abandons
        // the staged item move). Neither call touches anything outside this claim.
        foreach (var entity in new object?[] { row, item })
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
                "Could not fully roll back the staged cash shop claim for {Player}: the context may still hold a pending row removal or item move. {FailureCount} step(s) failed; subsequent SaveChangesAsync calls on this context must be treated as unsafe.",
                player.Name,
                failures.Count);
            return false;
        }

        return true;
    }
}
