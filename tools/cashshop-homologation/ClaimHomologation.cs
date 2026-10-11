// <copyright file="ClaimHomologation.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Runs the real cash shop storage claim flow against the homologation PostgreSQL database and
/// prints the before/after evidence for every acceptance scenario.
/// </summary>
/// <remarks>
/// <para>
/// This is the same production path a connected client exercises (the <c>D2 0B</c> handler calls
/// <see cref="CashShopStorageClaimService.ClaimAsync"/>): the real catalog of the database, the
/// real buy/gift services, the real claim service, the real WCoin debit and the real EF unit of
/// work. No rule is reimplemented here; the tool only prepares the data, runs the production
/// services and captures the evidence.
/// </para>
/// <para>
/// Only homologation data is touched: the scenarios use the homologation accounts
/// (<c>cshomolo</c>/<c>cshomol2</c>), and the disposable rows created for the quantity,
/// inventory-full, double-claim, missing-definition and IDOR scenarios are removed at the end.
/// Items already delivered to a character inventory by a successful claim are homologation
/// evidence and stay.
/// </para>
/// </remarks>
public static class ClaimHomologation
{
    /// <summary>The 1-day Wizard Ring offer used as the purchase scenario (package 35).</summary>
    public const int BuyPackageProductSeq = 35;

    /// <summary>The category (product display sequence) of the purchase package.</summary>
    public const int BuyProductDisplaySeq = 31;

    /// <summary>The 1-day price variant of the purchase package (100 WCoinP).</summary>
    public const int BuyPriceSeq = 95;

    /// <summary>The item code of the purchase offer; group 13, number 20 (Wizard's Ring).</summary>
    public const ushort BuyItemCode = 6676;

    /// <summary>The Pet Panda offer used to differentiate the storage tabs (package 31).</summary>
    public const int TabGiftPackageProductSeq = 31;

    /// <summary>The category of the Pet Panda package.</summary>
    public const int TabGiftProductDisplaySeq = 30;

    /// <summary>The 1-day price variant of the Pet Panda package (150 WCoinP).</summary>
    public const int TabGiftPriceSeq = 99;

    /// <summary>The item code of the Pet Panda offer; group 13, number 80.</summary>
    public const ushort TabGiftItemCode = 6736;

    /// <summary>The storage item type byte sent by the client ('P' = product).</summary>
    public const byte ProductTypeByte = (byte)'P';

    /// <summary>Runs every claim homologation scenario and prints the evidence.</summary>
    /// <returns>0 when every scenario passed; 1 otherwise.</returns>
    public static async Task<int> RunAsync()
    {
        var loggerFactory = new NullLoggerFactory();
        await new ConfigFileDatabaseConnectionStringProvider().InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var provider = new PersistenceContextProvider(loggerFactory, null);

        var configuration = await LoadConfigurationAsync(provider).ConfigureAwait(false);
        if (configuration is null)
        {
            Console.Error.WriteLine("No GameConfiguration in the homologation database; run the seed step first.");
            return 1;
        }

        var mapInitializer = new MapInitializer(configuration, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var gameContext = new GameContext(
            configuration,
            provider,
            mapInitializer,
            loggerFactory,
            new PlugInManager(null, loggerFactory, null, null),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;

        var catalogService = new CashShopCatalogService(provider, new MemoryCache(new MemoryCacheOptions()), NullLogger<CashShopCatalogService>.Instance);
        var storageService = new CashShopStorageService(() => provider.CreateNewContext());
        var resolver = new CashShopSelectionResolver(catalogService, NullLogger<CashShopSelectionResolver>.Instance);
        var buyService = new CashShopBuyService(resolver, storageService, NullLogger<CashShopBuyService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
        var giftService = new CashShopGiftService(resolver, storageService, NullLogger<CashShopGiftService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
        var claimService = new CashShopStorageClaimService(NullLogger<CashShopStorageClaimService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance), () => provider.CreateNewContext());

        var senderAccountId = await GetAccountIdAsync(provider, Program.HomologationLoginName).ConfigureAwait(false);
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        if (senderAccountId == Guid.Empty || receiverAccountId == Guid.Empty)
        {
            Console.Error.WriteLine("The homologation accounts do not exist; run the tool without arguments (seed) and in 'gift' mode first.");
            return 1;
        }

        var run = new HomologationRun
        {
            Provider = provider,
            GameContext = gameContext,
            BuyService = buyService,
            GiftService = giftService,
            ClaimService = claimService,
            SenderAccountId = senderAccountId,
            ReceiverAccountId = receiverAccountId,
        };

        // Estado inicial conhecido: storage de ambas as contas vazio (linhas descartaveis de
        // runs anteriores removidas com seus itens) e carteiras com saldo conhecido.
        await ResetStorageAsync(run, senderAccountId).ConfigureAwait(false);
        await ResetStorageAsync(run, receiverAccountId).ConfigureAwait(false);
        await SetBalanceAsync(run, senderAccountId, Program.HomologationBalance).ConfigureAwait(false);
        await SetBalanceAsync(run, receiverAccountId, Program.HomologationBalance).ConfigureAwait(false);

        await RunScenarioAAsync(run).ConfigureAwait(false);
        await RunScenarioBAsync(run).ConfigureAwait(false);
        await RunScenarioItemSeqAsync(run).ConfigureAwait(false);
        await RunScenarioCAsync(run).ConfigureAwait(false);
        await RunScenarioDAsync(run).ConfigureAwait(false);
        await RunScenarioEAsync(run).ConfigureAwait(false);
        await RunScenarioFAsync(run).ConfigureAwait(false);
        await RunScenarioGAsync(run).ConfigureAwait(false);

        await CleanupDisposableAsync(run).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"=== Homologacao do claim: {run.Failures} cenario(s) com desvio ===");
        return run.Failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Cenario A: compra real (pacote 35, Wizard Ring) cria a linha no storage; o claim entrega
    /// o MESMO Item ao inventario, consome a linha e nao debita nada.
    /// </summary>
    private static async Task RunScenarioAAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario A: compra valida -> claim (cshomolo, pacote 35 Wizard Ring) ===");
        var player = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            Console.WriteLine("[A] Purchase -> Claim ........ FAIL (player nao disponivel)");
            run.Failures++;
            return;
        }

        var walletBefore = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var txBefore = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageBefore = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var inventoryBefore = await GetInventoryItemIdsAsync(run, Program.HomologationCharacterName).ConfigureAwait(false);

        var buyResult = await run.BuyService.ProcessPurchaseAsync(player, BuyPackageProductSeq, BuyProductDisplaySeq, BuyPriceSeq, BuyItemCode, 509, false).ConfigureAwait(false);

        var walletAfterBuy = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var txAfterBuy = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageAfterBuy = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var row = storageAfterBuy.FirstOrDefault();

        Console.WriteLine($"Compra: {buyResult.ErrorCode} - {buyResult.ErrorMessage}");
        Console.WriteLine($"Wallet: {walletBefore} -> {walletAfterBuy} | transacoes: {txBefore} -> {txAfterBuy} | storage: {storageBefore.Count} -> {storageAfterBuy.Count} | inventario: {inventoryBefore.Count} itens");

        if (!buyResult.Success
            || row is null
            || walletAfterBuy != walletBefore - 100
            || txAfterBuy != txBefore + 1
            || storageAfterBuy.Count != storageBefore.Count + 1)
        {
            Console.WriteLine("[A] Purchase -> Claim ........ FAIL (compra nao criou a linha como esperado)");
            run.Failures++;
            return;
        }

        var itemIdBefore = row.ItemId;
        Console.WriteLine($"Linha: StorageIndex={row.StorageIndex} ItemSeq={row.ItemSeq} ProductSeq={row.ProductSeq} PriceSeq={row.PriceSeq} CashPoint={row.CashPoint} isGift={row.IsGift} ItemId={itemIdBefore}");

        var claimResult = await run.ClaimService.ClaimAsync(player, row.StorageIndex, row.ItemSeq, ProductTypeByte).ConfigureAwait(false);

        var walletAfterClaim = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var txAfterClaim = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageAfterClaim = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var inventoryAfter = await GetInventoryItemIdsAsync(run, Program.HomologationCharacterName).ConfigureAwait(false);
        var deliveredItemIds = inventoryAfter.Where(id => !inventoryBefore.Contains(id)).ToList();

        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage}");
        Console.WriteLine($"Wallet: {walletAfterBuy} -> {walletAfterClaim} (delta do claim: {walletAfterClaim - walletAfterBuy}) | transacoes: {txAfterBuy} -> {txAfterClaim} | storage: {storageAfterBuy.Count} -> {storageAfterClaim.Count} | inventario: {inventoryBefore.Count} -> {inventoryAfter.Count}");
        Console.WriteLine($"ItemId entregue: {string.Join(", ", deliveredItemIds)} (antes do claim: {itemIdBefore})");

        var pass = claimResult.Success
            && storageAfterClaim.Count == storageAfterBuy.Count - 1
            && inventoryAfter.Count == inventoryBefore.Count + 1
            && itemIdBefore is not null
            && deliveredItemIds.Count == 1
            && deliveredItemIds[0] == itemIdBefore.Value
            && walletAfterClaim == walletAfterBuy
            && txAfterClaim == txAfterBuy;
        Console.WriteLine(pass
            ? "[A] Purchase -> Claim ........ PASS"
            : "[A] Purchase -> Claim ........ FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario B: gift real (pacote 35) cria a linha no storage do destinatario; o claim do
    /// destinatario entrega o item e nao debita ninguem.
    /// </summary>
    private static async Task RunScenarioBAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario B: gift -> claim pelo destinatario (cshomolo -> cshomol2, Wizard Ring) ===");
        await ResetStorageAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        await SetBalanceAsync(run, run.SenderAccountId, Program.HomologationBalance).ConfigureAwait(false);

        var sender = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (sender is null)
        {
            Console.WriteLine("[B] Gift -> Claim ............ FAIL (remetente nao disponivel)");
            run.Failures++;
            return;
        }

        var senderWalletBefore = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var receiverWalletBefore = await GetBalanceAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var txBefore = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageBefore = await GetStorageRowsAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var receiverInventoryBefore = await GetInventoryItemIdsAsync(run, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);

        var giftResult = await run.GiftService.ProcessGiftAsync(sender, BuyPackageProductSeq, BuyProductDisplaySeq, BuyPriceSeq, BuyItemCode, 509, false, GiftHomologation.ReceiverCharacterName, "Homologacao Fase 9").ConfigureAwait(false);

        var senderWalletAfterGift = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var txAfterGift = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageAfterGift = await GetStorageRowsAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var row = storageAfterGift.FirstOrDefault();

        Console.WriteLine($"Gift: {giftResult.ErrorCode} - {giftResult.ErrorMessage}");
        Console.WriteLine($"Wallet remetente: {senderWalletBefore} -> {senderWalletAfterGift} | transacoes: {txBefore} -> {txAfterGift} | storage do destinatario: {storageBefore.Count} -> {storageAfterGift.Count}");

        if (!giftResult.Success
            || row is null
            || senderWalletAfterGift != senderWalletBefore - 100
            || txAfterGift != txBefore + 1
            || storageAfterGift.Count != storageBefore.Count + 1)
        {
            Console.WriteLine("[B] Gift -> Claim ............ FAIL (gift nao criou a linha como esperado)");
            run.Failures++;
            return;
        }

        var itemIdBefore = row.ItemId;
        Console.WriteLine($"Linha: StorageIndex={row.StorageIndex} ItemSeq={row.ItemSeq} ProductSeq={row.ProductSeq} PriceSeq={row.PriceSeq} isGift={row.IsGift} ItemId={itemIdBefore}");

        var receiver = await HomologationPlayer.CreateAsync(run.GameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        if (receiver is null)
        {
            Console.WriteLine("[B] Gift -> Claim ............ FAIL (destinatario nao disponivel)");
            run.Failures++;
            return;
        }

        var claimResult = await run.ClaimService.ClaimAsync(receiver, row.StorageIndex, row.ItemSeq, ProductTypeByte).ConfigureAwait(false);

        var senderWalletAfterClaim = await GetBalanceAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var receiverWalletAfterClaim = await GetBalanceAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var txAfterClaim = await GetTransactionCountAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var storageAfterClaim = await GetStorageRowsAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var receiverInventoryAfter = await GetInventoryItemIdsAsync(run, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        var deliveredItemIds = receiverInventoryAfter.Where(id => !receiverInventoryBefore.Contains(id)).ToList();

        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage}");
        Console.WriteLine($"Wallet remetente: {senderWalletAfterGift} -> {senderWalletAfterClaim} | wallet destinatario: {receiverWalletBefore} -> {receiverWalletAfterClaim} | transacoes: {txAfterGift} -> {txAfterClaim} | storage: {storageAfterGift.Count} -> {storageAfterClaim.Count} | inventario do destinatario: {receiverInventoryBefore.Count} -> {receiverInventoryAfter.Count}");
        Console.WriteLine($"ItemId entregue: {string.Join(", ", deliveredItemIds)} (antes do claim: {itemIdBefore})");

        var pass = claimResult.Success
            && storageAfterClaim.Count == storageAfterGift.Count - 1
            && receiverInventoryAfter.Count == receiverInventoryBefore.Count + 1
            && itemIdBefore is not null
            && deliveredItemIds.Count == 1
            && deliveredItemIds[0] == itemIdBefore.Value
            && senderWalletAfterClaim == senderWalletAfterGift
            && receiverWalletAfterClaim == receiverWalletBefore
            && txAfterClaim == txAfterGift;
        Console.WriteLine(pass
            ? "[B] Gift -> Claim ............ PASS"
            : "[B] Gift -> Claim ............ FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario 12: a mesma conta mantem entradas nas duas abas (compra na 'S', presente na 'G');
    /// o D2 0B nao carrega a aba, entao o ItemSeq global tem de resolver cada linha sem troca.
    /// </summary>
    private static async Task RunScenarioItemSeqAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario 12: ItemSeq global entre abas (cshomolo compra Wizard Ring e recebe Pet Panda) ===");
        await ResetStorageAsync(run, run.SenderAccountId).ConfigureAwait(false);
        await ResetStorageAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        await SetBalanceAsync(run, run.SenderAccountId, Program.HomologationBalance).ConfigureAwait(false);
        await SetBalanceAsync(run, run.ReceiverAccountId, Program.HomologationBalance).ConfigureAwait(false);

        var owner = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        var gifter = await HomologationPlayer.CreateAsync(run.GameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        if (owner is null || gifter is null)
        {
            Console.WriteLine("[12] ItemSeq global .......... FAIL (players nao disponiveis)");
            run.Failures++;
            return;
        }

        // 1) cshomolo compra o Wizard Ring (aba 'S'); a linha fica SEM claim.
        var buyResult = await run.BuyService.ProcessPurchaseAsync(owner, BuyPackageProductSeq, BuyProductDisplaySeq, BuyPriceSeq, BuyItemCode, 509, false).ConfigureAwait(false);

        // 2) cshomol2 envia o Pet Panda para cshomolo (aba 'G').
        var giftResult = await run.GiftService.ProcessGiftAsync(gifter, TabGiftPackageProductSeq, TabGiftProductDisplaySeq, TabGiftPriceSeq, TabGiftItemCode, 509, false, Program.HomologationCharacterName, "ItemSeq global").ConfigureAwait(false);

        var rows = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var purchaseRow = rows.FirstOrDefault(row => !row.IsGift);
        var giftRow = rows.FirstOrDefault(row => row.IsGift);

        Console.WriteLine($"Compra: {buyResult.ErrorCode} | Gift: {giftResult.ErrorCode} - {giftResult.ErrorMessage}");
        if (purchaseRow is null || giftRow is null)
        {
            Console.WriteLine($"[12] ItemSeq global .......... FAIL (linhas: {rows.Count}; compra={(purchaseRow is not null)} presente={(giftRow is not null)})");
            run.Failures++;
            return;
        }

        Console.WriteLine($"Purchase tab: StorageIndex={purchaseRow.StorageIndex} ItemSeq={purchaseRow.ItemSeq} ItemId={purchaseRow.ItemId}");
        Console.WriteLine($"Gift tab:     StorageIndex={giftRow.StorageIndex} ItemSeq={giftRow.ItemSeq} ItemId={giftRow.ItemId}");
        var itemSeqDistinct = purchaseRow.ItemSeq != giftRow.ItemSeq;
        Console.WriteLine($"ItemSeq global: purchase={purchaseRow.ItemSeq}, gift={giftRow.ItemSeq}, distintos={itemSeqDistinct}");

        if (!itemSeqDistinct)
        {
            Console.WriteLine("[12] ItemSeq global .......... FAIL (par identico nas duas abas: identidade ambigua)");
            run.Failures++;
            return;
        }

        var inventoryBefore = await GetInventoryItemIdsAsync(run, Program.HomologationCharacterName).ConfigureAwait(false);
        var claimPurchase = await run.ClaimService.ClaimAsync(owner, purchaseRow.StorageIndex, purchaseRow.ItemSeq, ProductTypeByte).ConfigureAwait(false);
        var claimGift = await run.ClaimService.ClaimAsync(owner, giftRow.StorageIndex, giftRow.ItemSeq, ProductTypeByte).ConfigureAwait(false);

        var inventoryDefinitions = await GetInventoryDefinitionsAsync(run, Program.HomologationCharacterName).ConfigureAwait(false);
        var rowsAfter = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        Console.WriteLine($"Claims: purchase={claimPurchase.ErrorCode} gift={claimGift.ErrorCode} | storage: {rows.Count} -> {rowsAfter.Count} | inventario: {inventoryBefore.Count} -> {inventoryDefinitions.Count}");
        Console.WriteLine($"Itens no inventario (group/number): {string.Join(", ", inventoryDefinitions.Select(definition => $"{definition.Group}/{definition.Number}"))}");

        var pass = itemSeqDistinct
            && claimPurchase.Success
            && claimGift.Success
            && rowsAfter.Count == rows.Count - 2
            && inventoryDefinitions.Count == inventoryBefore.Count + 2
            && inventoryDefinitions.Any(definition => definition.Group == 13 && definition.Number == 20)
            && inventoryDefinitions.Any(definition => definition.Group == 13 && definition.Number == 80);
        Console.WriteLine(pass
            ? "[12] ItemSeq global .......... PASS"
            : "[12] ItemSeq global .......... FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario C: item stackavel real (Splinter of Armor, 13/32, capacidade 20) com a Durability
    /// que uma compra com Quantity grava; o claim preserva a Durability no inventario.
    /// </summary>
    private static async Task RunScenarioCAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario C: Quantity (Splinter of Armor 13/32, capacidade 20, Durability 15) ===");

        // Nenhuma oferta real do catalogo com Quantity>1 aponta para um item stackavel existente
        // na configuracao Season 6 (a mesma incompatibilidade aceita do item 6702). A linha e
        // criada com o item REAL da configuracao e a Durability que a regra de producao
        // (ResolveDeliveredDurabilities) grava para Quantity 15 <= capacidade 20.
        var player = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            Console.WriteLine("[C] Quantity ................. FAIL (player nao disponivel)");
            run.Failures++;
            return;
        }

        var definition = await LoadItemDefinitionAsync(run, 13, 32).ConfigureAwait(false);
        if (definition is null)
        {
            Console.WriteLine("[C] Quantity ................. FAIL (Splinter of Armor 13/32 ausente na configuracao)");
            run.Failures++;
            return;
        }

        var (storageIndex, itemSeq, itemId) = await CreateDisposableRowAsync(run, run.SenderAccountId, isGift: false, definition, durability: 15).ConfigureAwait(false);
        Console.WriteLine($"Linha descartavel: StorageIndex={storageIndex} ItemSeq={itemSeq} ItemId={itemId} Durability=15");

        var claimResult = await run.ClaimService.ClaimAsync(player, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);

        var durabilityAfter = await GetInventoryItemDurabilityAsync(run, Program.HomologationCharacterName, itemId).ConfigureAwait(false);
        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage} | Durability no inventario: {durabilityAfter}");

        var pass = claimResult.Success && durabilityAfter == 15;
        Console.WriteLine(pass
            ? "[C] Quantity ................. PASS (15 == 15)"
            : "[C] Quantity ................. FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario D: inventario cheio (preenchido legitimamente com itens da MESMA definicao do
    /// item reclamado, em memoria, nunca salvos); o claim e recusado com o codigo 21 e a linha
    /// permanece.
    /// </summary>
    private static async Task RunScenarioDAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario D: inventario cheio (cshomol2, claim de linha descartavel) ===");
        var player = await HomologationPlayer.CreateAsync(run.GameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            Console.WriteLine("[D] Inventory Full ........... FAIL (player nao disponivel)");
            run.Failures++;
            return;
        }

        var stackable = await LoadItemDefinitionAsync(run, 13, 32).ConfigureAwait(false);
        if (stackable is null)
        {
            Console.WriteLine("[D] Inventory Full ........... FAIL (definicao ausente)");
            run.Failures++;
            return;
        }

        // Preenche o inventario EM MEMORIA com itens da MESMA definicao do item reclamado (nunca
        // salvos): quando um item 13/32 nao encontra mais espaco, o claim do splinter 13/32 sera
        // recusado com o codigo 21 — o preenchimento com a mesma dimensao garante o resultado.
        var fills = 0;
        while (true)
        {
            var fill = player.DataContext.CreateNew<Item>();
            fill.Definition = stackable;
            fill.Durability = 5;
            if (player.Inventory!.CheckInvSpace(fill) is not { } slot)
            {
                break;
            }

            await player.Inventory.AddItemAsync(slot, fill).ConfigureAwait(false);
            fills++;
        }

        Console.WriteLine($"Inventario preenchido com {fills} itens 13/32 em memoria (nunca salvos).");

        var (storageIndex, itemSeq, itemId) = await CreateDisposableRowAsync(run, run.ReceiverAccountId, isGift: false, stackable, durability: 5).ConfigureAwait(false);
        run.DisposableItemIds.Add(itemId);
        Console.WriteLine($"Linha descartavel: StorageIndex={storageIndex} ItemSeq={itemSeq} ItemId={itemId} Durability=5");

        var walletBefore = await GetBalanceAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var txBefore = await GetTransactionCountAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var claimResult = await run.ClaimService.ClaimAsync(player, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);
        var walletAfter = await GetBalanceAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var txAfter = await GetTransactionCountAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var storageAfter = await GetStorageRowsAsync(run, run.ReceiverAccountId).ConfigureAwait(false);
        var row = storageAfter.FirstOrDefault(candidate => candidate.ItemId == itemId);

        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage} | wallet: {walletBefore} -> {walletAfter} | transacoes: {txBefore} -> {txAfter} | storage com a linha: {row is not null}");

        var pass = !claimResult.Success
            && claimResult.ErrorCode == StorageClaimErrorCode.InventoryFull
            && row is not null
            && row.ItemId == itemId
            && walletAfter == walletBefore
            && txAfter == txBefore;
        Console.WriteLine(pass
            ? "[D] Inventory Full ........... PASS (codigo 21, storage preservado)"
            : "[D] Inventory Full ........... FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario E: exatamente uma linha; dois claims contra o mesmo par — o primeiro entrega, o
    /// segundo nao encontra mais a linha.
    /// </summary>
    private static async Task RunScenarioEAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario E: duplo claim (cshomolo, linha descartavel) ===");
        var player = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            Console.WriteLine("[E] Double Claim ............. FAIL (player nao disponivel)");
            run.Failures++;
            return;
        }

        var (storageIndex, itemSeq, itemId) = await CreateDisposableRowAsync(run, run.SenderAccountId, isGift: false, await LoadItemDefinitionAsync(run, 13, 32).ConfigureAwait(false), durability: 5).ConfigureAwait(false);
        Console.WriteLine($"Linha descartavel: StorageIndex={storageIndex} ItemSeq={itemSeq} ItemId={itemId} Durability=5");

        var first = await run.ClaimService.ClaimAsync(player, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);
        var second = await run.ClaimService.ClaimAsync(player, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);
        var storageAfter = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);

        Console.WriteLine($"Primeiro claim: {first.ErrorCode} | segundo claim: {second.ErrorCode} | storage final: {storageAfter.Count} linha(s)");

        var pass = first.Success && !second.Success && second.ErrorCode == StorageClaimErrorCode.StorageNotFound && storageAfter.Count == 0;
        Console.WriteLine(pass
            ? "[E] Double Claim ............. PASS (1 entrega, segunda recusada, storage 0)"
            : "[E] Double Claim ............. FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario F: item sem ItemDefinition (a forma aceita da incompatibilidade 6702): o claim e
    /// recusado, a linha e o item permanecem, nenhuma excecao escapa.
    /// </summary>
    private static async Task RunScenarioFAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario F: item sem ItemDefinition (cshomolo, linha descartavel) ===");
        var player = await HomologationPlayer.CreateAsync(run.GameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            Console.WriteLine("[F] Missing Definition ....... FAIL (player nao disponivel)");
            run.Failures++;
            return;
        }

        var (storageIndex, itemSeq, itemId) = await CreateDisposableRowAsync(run, run.SenderAccountId, isGift: false, definition: null, durability: 0).ConfigureAwait(false);
        run.DisposableItemIds.Add(itemId);
        Console.WriteLine($"Linha descartavel: StorageIndex={storageIndex} ItemSeq={itemSeq} ItemId={itemId} (Definition ausente)");

        var claimResult = await run.ClaimService.ClaimAsync(player, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);
        var storageAfter = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        // A linha sem item nao tem ItemId para casar: identifica pela dupla (StorageIndex, ItemSeq).
        var row = storageAfter.FirstOrDefault(candidate => candidate.StorageIndex == storageIndex && candidate.ItemSeq == itemSeq);
        var inventoryAfter = await GetInventoryItemIdsAsync(run, Program.HomologationCharacterName).ConfigureAwait(false);

        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage} | storage com a linha: {row is not null} | inventario: {inventoryAfter.Count} itens");

        var pass = !claimResult.Success
            && claimResult.ErrorCode == StorageClaimErrorCode.CannotUse
            && row is not null
            && row.ItemId is null;
        Console.WriteLine(pass
            ? "[F] Missing Definition ....... PASS (linha e item preservados)"
            : "[F] Missing Definition ....... FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Cenario G: a linha pertence a cshomolo; o jogador de cshomol2 tenta o claim com o par da
    /// outra conta — recusa sem efeito, sem revelar a existencia do item.
    /// </summary>
    private static async Task RunScenarioGAsync(HomologationRun run)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario G: IDOR — claim de outra conta (cshomol2 tenta a linha de cshomolo) ===");
        var ownerRowsBefore = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var (storageIndex, itemSeq, itemId) = await CreateDisposableRowAsync(run, run.SenderAccountId, isGift: false, await LoadItemDefinitionAsync(run, 13, 20).ConfigureAwait(false), durability: 30).ConfigureAwait(false);
        run.DisposableItemIds.Add(itemId);
        Console.WriteLine($"Linha do dono: StorageIndex={storageIndex} ItemSeq={itemSeq} ItemId={itemId}");

        var attacker = await HomologationPlayer.CreateAsync(run.GameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        if (attacker is null)
        {
            Console.WriteLine("[G] IDOR ..................... FAIL (player da outra conta nao disponivel)");
            run.Failures++;
            return;
        }

        var attackerInventoryBefore = await GetInventoryItemIdsAsync(run, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        var claimResult = await run.ClaimService.ClaimAsync(attacker, storageIndex, itemSeq, ProductTypeByte).ConfigureAwait(false);
        var ownerRowsAfter = await GetStorageRowsAsync(run, run.SenderAccountId).ConfigureAwait(false);
        var row = ownerRowsAfter.FirstOrDefault(candidate => candidate.ItemId == itemId);
        var attackerInventoryAfter = await GetInventoryItemIdsAsync(run, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);

        Console.WriteLine($"Claim: {claimResult.ErrorCode} - {claimResult.ErrorMessage} | storage do dono: {ownerRowsBefore.Count + 1} -> {ownerRowsAfter.Count} | inventario do atacante: {attackerInventoryBefore.Count} -> {attackerInventoryAfter.Count}");

        var pass = !claimResult.Success
            && claimResult.ErrorCode == StorageClaimErrorCode.StorageNotFound
            && row is not null
            && row.ItemId == itemId
            && attackerInventoryAfter.Count == attackerInventoryBefore.Count;
        Console.WriteLine(pass
            ? "[G] IDOR ..................... PASS (recusado como 'nao encontrado', dono intacto)"
            : "[G] IDOR ..................... FAIL");
        if (!pass)
        {
            run.Failures++;
        }
    }

    /// <summary>
    /// Remove as linhas descartaveis ainda existentes deletando seus itens (o cascade Item→linha
    /// remove a linha junto) e verifica que nada delas sobrou.
    /// </summary>
    private static async Task CleanupDisposableAsync(HomologationRun run)
    {
        if (run.DisposableItemIds.Count == 0)
        {
            return;
        }

        using var context = run.Provider.CreateNewContext();
        var items = await context.GetAsync<Item>().ConfigureAwait(false);
        foreach (var itemId in run.DisposableItemIds)
        {
            var item = items.FirstOrDefault(candidate => candidate.GetId() == itemId);
            if (item is not null)
            {
                await context.DeleteAsync(item).ConfigureAwait(false);
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Limpeza: {run.DisposableItemIds.Count} item(ns) descartavel(is) removido(s) (linhas via cascade).");
    }

    /// <summary>
    /// Cria uma linha de storage descartavel (com item opcional) e retorna sua identificacao.
    /// O ItemSeq espelha a reserva de producao: sequencia unica por conta nas duas abas.
    /// </summary>
    private static async Task<(int StorageIndex, int ItemSeq, Guid ItemId)> CreateDisposableRowAsync(
        HomologationRun run,
        Guid accountId,
        bool isGift,
        ItemDefinition? definition,
        double durability)
    {
        // O contexto do player (AccountContext) ignora tipos de configuracao: atribuir a Definition
        // aqui persiste apenas a FK (DefinitionId), sem anexar o grafo de configuracao da definicao
        // (classes, juncoes, skills) — o mesmo comportamento da compra e do gift de producao. O
        // contexto completo (CreateNewContext) tentaria anexar esse grafo e conflita no tracking.
        using var context = run.Provider.CreateNewPlayerContext(run.GameContext.Configuration);
        var rows = (await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false))
            .Where(row => row.AccountId == accountId)
            .ToList();
        var tabRows = rows.Where(row => row.IsGift == isGift).ToList();
        var usedIndexes = tabRows.Select(row => row.StorageIndex).ToHashSet();
        var storageIndex = 0;
        while (usedIndexes.Contains(storageIndex))
        {
            storageIndex++;
        }

        var itemSeq = rows.Count == 0 ? 0 : rows.Max(row => row.ItemSeq) + 1;

        var storageItem = context.CreateNew<CashShopStorageItem>();
        storageItem.AccountId = accountId;
        storageItem.StorageIndex = storageIndex;
        storageItem.ItemSeq = itemSeq;
        storageItem.IsGift = isGift;
        storageItem.StorageGroup = 673;
        storageItem.ProductSeq = 58;
        storageItem.PriceSeq = 95;
        storageItem.CashPoint = 100;
        storageItem.ItemType = CashShopStorageItemType.Product;
        if (definition is not null)
        {
            var item = context.CreateNew<Item>();
            item.Definition = definition;
            item.Durability = durability;
            storageItem.Item = item;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        var itemId = storageItem.Item?.GetId() ?? Guid.Empty;
        if (itemId != Guid.Empty)
        {
            run.DisposableItemIds.Add(itemId);
        }

        return (storageIndex, itemSeq, itemId);
    }

    /// <summary>
    /// Limpa o storage de uma conta: linhas com item sao removidas pelo cascade do item;
    /// linhas sem item sao removidas diretamente.
    /// </summary>
    private static async Task ResetStorageAsync(HomologationRun run, Guid accountId)
    {
        using var context = run.Provider.CreateNewContext();
        var rows = (await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false))
            .Where(row => row.AccountId == accountId)
            .ToList();
        foreach (var row in rows)
        {
            if (row.Item is { } item)
            {
                await context.DeleteAsync(item).ConfigureAwait(false);
            }
            else
            {
                await context.DeleteAsync(row).ConfigureAwait(false);
            }
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Reset do storage de {accountId}: {rows.Count} linha(s) removida(s).");
    }

    /// <summary>Carrega a configuracao do jogo do banco de homologacao.</summary>
    private static async Task<GameConfiguration?> LoadConfigurationAsync(IPersistenceContextProvider provider)
    {
        using var configurationContext = provider.CreateNewConfigurationContext();
        return (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>Carrega uma definicao de item (group/number) em um contexto fresco.</summary>
    private static async Task<ItemDefinition?> LoadItemDefinitionAsync(HomologationRun run, int group, int number)
    {
        using var context = run.Provider.CreateNewContext();
        var definitions = await context.GetAsync<ItemDefinition>().ConfigureAwait(false);
        return definitions.FirstOrDefault(candidate => candidate.Group == group && candidate.Number == number);
    }

    /// <summary>Le o saldo WCoin de uma conta.</summary>
    private static async Task<long> GetBalanceAsync(HomologationRun run, Guid accountId)
    {
        using var context = run.Provider.CreateNewContext();
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    /// <summary>Escreve o saldo WCoin de uma conta.</summary>
    private static async Task SetBalanceAsync(HomologationRun run, Guid accountId, long balance)
    {
        using var context = run.Provider.CreateNewContext();
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        if (wallet is null)
        {
            return;
        }

        wallet.Balance = balance;
        wallet.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Conta as transacoes WCoin de uma conta.</summary>
    private static async Task<int> GetTransactionCountAsync(HomologationRun run, Guid accountId)
    {
        using var context = run.Provider.CreateNewContext();
        var transactions = await context.GetAsync<WCoinTransaction>().ConfigureAwait(false);
        return transactions.Count(transaction => transaction.AccountId == accountId);
    }

    /// <summary>Le as linhas de storage de uma conta, com o item entregue (ItemId).</summary>
    private static async Task<List<StorageRowSnapshot>> GetStorageRowsAsync(HomologationRun run, Guid accountId)
    {
        using var context = run.Provider.CreateNewContext();
        var rows = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return rows
            .Where(row => row.AccountId == accountId)
            .Select(row => new StorageRowSnapshot(
                row.StorageIndex,
                row.ItemSeq,
                row.ProductSeq,
                row.PriceSeq,
                row.CashPoint,
                row.IsGift,
                row.Item is null ? null : row.Item.GetId()))
            .ToList();
    }

    /// <summary>Le os ids dos itens do inventario de um personagem.</summary>
    private static async Task<List<Guid>> GetInventoryItemIdsAsync(HomologationRun run, string characterName)
    {
        using var context = run.Provider.CreateNewContext();
        var characters = await context.GetAsync<Character>().ConfigureAwait(false);
        var character = characters.FirstOrDefault(candidate => candidate.Name == characterName);
        return character?.Inventory?.Items.Select(item => item.GetId()).ToList() ?? new List<Guid>();
    }

    /// <summary>Le os pares (group/number) das definicoes dos itens do inventario de um personagem.</summary>
    private static async Task<List<(int Group, int Number)>> GetInventoryDefinitionsAsync(HomologationRun run, string characterName)
    {
        using var context = run.Provider.CreateNewContext();
        var characters = await context.GetAsync<Character>().ConfigureAwait(false);
        var character = characters.FirstOrDefault(candidate => candidate.Name == characterName);
        return character?.Inventory?.Items
            .Select(item => (Group: (int)(item.Definition?.Group ?? -1), Number: (int)(item.Definition?.Number ?? -1)))
            .ToList() ?? new List<(int Group, int Number)>();
    }

    /// <summary>Le a Durability de um item do inventario de um personagem pelo id.</summary>
    private static async Task<double?> GetInventoryItemDurabilityAsync(HomologationRun run, string characterName, Guid itemId)
    {
        using var context = run.Provider.CreateNewContext();
        var characters = await context.GetAsync<Character>().ConfigureAwait(false);
        var character = characters.FirstOrDefault(candidate => candidate.Name == characterName);
        return character?.Inventory?.Items
            .Where(item => item.GetId() == itemId)
            .Select(item => (double?)item.Durability)
            .FirstOrDefault();
    }

    /// <summary>Le o id da conta pelo login.</summary>
    private static async Task<Guid> GetAccountIdAsync(IPersistenceContextProvider provider, string loginName)
    {
        using var context = provider.CreateNewContext();
        var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
        return accounts.FirstOrDefault(account => account.LoginName == loginName)?.Id ?? Guid.Empty;
    }

    /// <summary>Contagem de itens por definicao (group/number) no banco — prova de nao-duplicacao.</summary>
    private static async Task<int> GetItemCountByDefinitionAsync(HomologationRun run, int group, int number)
    {
        using var context = run.Provider.CreateNewContext();
        var items = await context.GetAsync<Item>().ConfigureAwait(false);
        return items.Count(item => item.Definition?.Group == group && item.Definition?.Number == number);
    }

    /// <summary>Snapshot de uma linha do storage da conta.</summary>
    private sealed record StorageRowSnapshot(
        int StorageIndex,
        int ItemSeq,
        int ProductSeq,
        int PriceSeq,
        double CashPoint,
        bool IsGift,
        Guid? ItemId);

    /// <summary>O estado compartilhado da rodada de homologacao.</summary>
    private sealed class HomologationRun
    {
        /// <summary>Gets or sets the persistence provider.</summary>
        public IPersistenceContextProvider Provider { get; init; } = null!;

        /// <summary>Gets or sets the game context.</summary>
        public IGameContext GameContext { get; init; } = null!;

        /// <summary>Gets or sets the real buy service.</summary>
        public CashShopBuyService BuyService { get; init; } = null!;

        /// <summary>Gets or sets the real gift service.</summary>
        public CashShopGiftService GiftService { get; init; } = null!;

        /// <summary>Gets or sets the real claim service.</summary>
        public CashShopStorageClaimService ClaimService { get; init; } = null!;

        /// <summary>Gets or sets the sender's account id (cshomolo).</summary>
        public Guid SenderAccountId { get; init; }

        /// <summary>Gets or sets the receiver's account id (cshomol2).</summary>
        public Guid ReceiverAccountId { get; init; }

        /// <summary>Gets the item ids created as disposable data (removed at cleanup).</summary>
        public List<Guid> DisposableItemIds { get; } = new();

        /// <summary>Gets or sets the number of scenarios that deviated from the expectation.</summary>
        public int Failures { get; set; }
    }
}
