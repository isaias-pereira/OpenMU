// <copyright file="GiftStorageMetadataHomologation.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.PlugIns;
using Nito.AsyncEx;

/// <summary>
/// Homologation of the Fase 10 gift metadata (sender/message) and the D2 0E gift storage
/// packet, against the real PostgreSQL homologation database and the real production services.
/// </summary>
/// <remarks>
/// <para>
/// Like the other homologation flows, this runner never reimplements a rule: the gift goes
/// through the real <see cref="CashShopGiftService.ProcessGiftAsync"/> (the D2 04 handler path),
/// the listing through the real <see cref="CashShopStorageListAction.RequestListAsync"/> (the
/// D2 05 handler path) and the claim through the real <see cref="CashShopStorageClaimService"/>.
/// </para>
/// <para>
/// <b>Packet capture scope.</b> The captured bytes come from the real listing action and the
/// real D2 0E/D2 0D serializers over an in-process <c>SpyConnection</c> (a pipe pair), not from
/// a real network socket and not from a connected Main.exe. The production
/// <c>CashShopViewPlugIn</c> only forwards to the very same extension methods this capture
/// observes, so the bytes are identical to what a connected client would receive; the socket
/// hop itself is not covered here.
/// </para>
/// <para>
/// Only disposable homologation data is touched (accounts <c>cshomolo</c>/<c>cshomol2</c> and
/// rows created by this runner); <c>DB_NAME</c> selects the validation database.
/// </para>
/// </remarks>
public static class GiftStorageMetadataHomologation
{
    /// <summary>The PT-BR accented message sent with the metadata gift.</summary>
    public const string GiftMessagePtBr = "Olá, você ganhou um presente!";

    /// <summary>Cash point marker of the synthetic foreign-account row used in the scope check.</summary>
    private const double ForeignRowCashPoint = 123456.0;

    /// <summary>Identity of a data-model item through <see cref="IIdentifiable"/>.</summary>
    private static Guid? ItemIdOf(Item? item) => (item as Persistence.IIdentifiable)?.Id;

    /// <summary>Decodes strictly: throws on any malformed UTF-8 sequence.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Runs every gift-metadata homologation scenario and prints the evidence.</summary>
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
        var giftService = new CashShopGiftService(resolver, storageService, NullLogger<CashShopGiftService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
        var claimService = new CashShopStorageClaimService(NullLogger<CashShopStorageClaimService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance), () => provider.CreateNewContext());
        var listAction = new CashShopStorageListAction(storageService);

        var failures = 0;
        try
        {
            failures += await ScenarioGiftWithMetadataAsync(provider, giftService, gameContext).ConfigureAwait(false);
            failures += await ScenarioD2_0ECaptureAsync(provider, listAction, giftService, gameContext).ConfigureAwait(false);
            failures += await ScenarioLegacyAndPurchaseCaptureAsync(provider, listAction, gameContext).ConfigureAwait(false);
            failures += await ScenarioGiftToClaimAsync(provider, claimService, giftService, gameContext).ConfigureAwait(false);
            failures += await ScenarioNegativeMessagesAsync(provider, giftService, gameContext).ConfigureAwait(false);
            failures += await ScenarioOtherAccountScopeAsync(provider, listAction, giftService, gameContext).ConfigureAwait(false);
        }
        finally
        {
            await CleanupAsync(provider).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine($"=== Homologacao Fase 10 (metadados + D2 0E): {failures} cenario(s) com desvio ===");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Cenário G (ETAPA 7): gift real com mensagem PT-BR acentuada — débito único, linha criada,
    /// GiftSender = personagem autenticado do remetente, GiftMessage persistida, ItemId na linha.
    /// </summary>
    private static async Task<int> ScenarioGiftWithMetadataAsync(IPersistenceContextProvider provider, CashShopGiftService giftService, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario G: gift com metadados (mensagem PT-BR acentuada) ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        var senderAccountId = await GetAccountIdAsync(provider, Program.HomologationLoginName).ConfigureAwait(false);
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        var balanceBefore = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        var storageBefore = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        var transactionsBefore = await GetTransactionCountAsync(provider, senderAccountId).ConfigureAwait(false);

        var result = await SendGiftAsync(giftService, gameContext, GiftMessagePtBr).ConfigureAwait(false);
        Console.WriteLine($"Resultado: {result.ErrorCode} - {result.ErrorMessage}");

        var row = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        var balanceAfter = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        var storageAfter = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        var transactionsAfter = await GetTransactionCountAsync(provider, senderAccountId).ConfigureAwait(false);
        var price = await GetGiftPriceAsync(provider).ConfigureAwait(false);

        Console.WriteLine($"WCoin antes: {balanceBefore} | depois: {balanceAfter} | preco: {price}");
        Console.WriteLine($"WCoinTransaction do remetente antes: {transactionsBefore} | depois: {transactionsAfter}");
        Console.WriteLine($"Storage do destinatario antes: {storageBefore} | depois: {storageAfter}");
        if (row is not null)
        {
            Console.WriteLine($"Linha criada: StorageIndex={row.StorageIndex} ItemSeq={row.ItemSeq} ItemId={ItemIdOf(row.Item)} IsGift={row.IsGift} ItemType={row.ItemType}");
            Console.WriteLine($"GiftSender persistido: '{row.GiftSender}' | GiftMessage persistida: '{row.GiftMessage}'");
        }

        var pass = true;
        if (result is null || !result.Success)
        {
            Console.WriteLine("DESVIO: o gift não foi aceito.");
            pass = false;
        }

        if (row is null)
        {
            Console.WriteLine("DESVIO: nenhuma linha de presente foi criada no PostgreSQL.");
            pass = false;
        }
        else
        {
            if (row.GiftSender != Program.HomologationCharacterName)
            {
                Console.WriteLine($"DESVIO: GiftSender esperado '{Program.HomologationCharacterName}', obtido '{row.GiftSender}'.");
                pass = false;
            }

            if (row.GiftMessage != GiftMessagePtBr)
            {
                Console.WriteLine($"DESVIO: GiftMessage esperada '{GiftMessagePtBr}', obtida '{row.GiftMessage}'.");
                pass = false;
            }

            if (ItemIdOf(row.Item) is null)
            {
                Console.WriteLine("DESVIO: a linha do presente não referencia o item entregue (ItemId nulo).");
                pass = false;
            }

            if (!row.IsGift)
            {
                Console.WriteLine("DESVIO: a linha não está marcada como presente (IsGift = false).");
                pass = false;
            }
        }

        if (balanceAfter != balanceBefore - price)
        {
            Console.WriteLine($"DESVIO: débito esperado de {price}, obtido {balanceBefore - balanceAfter}.");
            pass = false;
        }

        if (transactionsAfter != transactionsBefore + 1)
        {
            Console.WriteLine($"DESVIO: esperada exatamente 1 WCoinTransaction nova, obtidas {transactionsAfter - transactionsBefore}.");
            pass = false;
        }

        if (storageAfter != storageBefore + 1)
        {
            Console.WriteLine("DESVIO: a contagem do storage do destinatário não cresceu exatamente 1.");
            pass = false;
        }

        return pass ? 0 : 1;
    }

    /// <summary>
    /// Cenário H (ETAPA 8): captura dos bytes reais do D2 0E pelo caminho de listagem
    /// (D2 05 → <see cref="CashShopStorageListAction"/> → view → serializer), sobre uma
    /// conexão de homologação (SpyConnection) — não é captura de rede real.
    /// </summary>
    private static async Task<int> ScenarioD2_0ECaptureAsync(IPersistenceContextProvider provider, CashShopStorageListAction listAction, CashShopGiftService giftService, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario H: captura binaria do D2 0E (caminho real de listagem, SpyConnection) ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        var result = await SendGiftAsync(giftService, gameContext, GiftMessagePtBr).ConfigureAwait(false);
        if (result is null || !result.Success)
        {
            Console.WriteLine($"DESVIO: o gift de preparação falhou: {result?.ErrorMessage}");
            return 1;
        }

        using var receiver = await CreateCapturingPlayerAsync(gameContext).ConfigureAwait(false);
        if (receiver is null)
        {
            Console.Error.WriteLine("O jogador capturador não pôde ser criado; rode o seed primeiro.");
            return 1;
        }

        var view = receiver.View;
        await listAction.RequestListAsync(receiver, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);
        var packets = view.Collect();
        var counts = packets.Where(p => p.Length == 12 && p[2] == 0xD2 && p[3] == 0x06).ToList();
        var giftLines = packets.Where(p => p.Length == 244 && p[2] == 0xD2 && p[3] == 0x0E).ToList();
        var purchaseLines = packets.Where(p => p.Length == 33 && p[2] == 0xD2 && p[3] == 0x0D).ToList();

        Console.WriteLine($"Pacotes capturados: {packets.Count} no total | D2 06: {counts.Count} | D2 0E: {giftLines.Count} | D2 0D: {purchaseLines.Count}");

        var row = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        var pass = true;
        if (counts.Count != 1)
        {
            Console.WriteLine("DESVIO: a resposta deve conter exatamente um D2 06.");
            pass = false;
        }

        if (giftLines.Count != 1)
        {
            Console.WriteLine($"DESVIO: esperado exatamente 1 pacote D2 0E, obtidos {giftLines.Count}.");
            pass = false;
        }

        if (purchaseLines.Count != 0)
        {
            Console.WriteLine("DESVIO: a aba de presentes não pode emitir linhas D2 0D.");
            pass = false;
        }

        if (row is not null && giftLines.Count == 1)
        {
            var bytes = giftLines[0];
            var senderField = bytes.AsSpan(33, 11).ToArray();
            var messageField = bytes.AsSpan(44, 200).ToArray();
            var senderUsed = Array.IndexOf(senderField, (byte)0);
            var messageUsed = Array.IndexOf(messageField, (byte)0);
            var senderText = StrictUtf8.GetString(senderField, 0, senderUsed);
            var messageText = StrictUtf8.GetString(messageField, 0, messageUsed);
            Console.WriteLine($"Header: {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}");
            Console.WriteLine($"StorageIndex={BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4))} ItemSeq={BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8))} ProductSeq={BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16))} PriceSeq={BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20))} CashPoint={BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(24))} ItemType='{(char)bytes[32]}'");
            Console.WriteLine($"Sender no fio ({senderUsed} bytes uteis): '{senderText}' | Mensagem no fio ({messageUsed} bytes uteis): '{messageText}'");

            if (bytes[0] != 0xC1 || bytes[1] != 244 || bytes[2] != 0xD2 || bytes[3] != 0x0E)
            {
                Console.WriteLine("DESVIO: cabeçalho do D2 0E incorreto.");
                pass = false;
            }

            if (senderText != Program.HomologationCharacterName || messageText != GiftMessagePtBr)
            {
                Console.WriteLine("DESVIO: remetente/mensagem no fio diferem do persistido.");
                pass = false;
            }

            if (senderUsed > 10 || messageUsed > 199)
            {
                Console.WriteLine("DESVIO: campo de texto excedeu os bytes úteis permitidos.");
                pass = false;
            }

            if (senderField[10] != 0 || messageField[199] != 0)
            {
                Console.WriteLine("DESVIO: o último byte dos campos de texto não é NUL.");
                pass = false;
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != row.StorageIndex
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) != row.ItemSeq
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)) != row.ProductSeq
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)) != row.PriceSeq
                || Math.Abs(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(24)) - row.CashPoint) > 0.0001)
            {
                Console.WriteLine("DESVIO: campos base do D2 0E diferem da linha persistida.");
                pass = false;
            }
        }

        return pass ? 0 : 1;
    }

    /// <summary>
    /// Cenário I (ETAPAS 9/10): presente legado (metadados NULL) sai pelo D2 0E com campos
    /// vazios e seguros; a aba de compras segue emitindo só D2 0D.
    /// </summary>
    private static async Task<int> ScenarioLegacyAndPurchaseCaptureAsync(IPersistenceContextProvider provider, CashShopStorageListAction listAction, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario I: presente legado (NULL) e aba de compras (D2 0D) ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);

        // Linha legada: como as entregues antes da Fase 10 — sem metadados.
        using (var context = provider.CreateNewContext())
        {
            var legacy = context.CreateNew<CashShopStorageItem>();
            legacy.AccountId = receiverAccountId;
            legacy.StorageIndex = 0;
            legacy.ItemSeq = 0;
            legacy.StorageGroup = 673;
            legacy.ProductSeq = 58;
            legacy.PriceSeq = 95;
            legacy.CashPoint = 100;
            legacy.ItemType = CashShopStorageItemType.Product;
            legacy.IsGift = true;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        using var receiver = await CreateCapturingPlayerAsync(gameContext).ConfigureAwait(false);
        if (receiver is null)
        {
            Console.Error.WriteLine("O jogador capturador não pôde ser criado; rode o seed primeiro.");
            return 1;
        }

        var view = receiver.View;

        await listAction.RequestListAsync(receiver, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);
        var giftPackets = view.Collect();
        var giftLines = giftPackets.Where(p => p.Length == 244 && p[2] == 0xD2 && p[3] == 0x0E).ToList();
        var gift0D = giftPackets.Where(p => p.Length == 33 && p[2] == 0xD2 && p[3] == 0x0D).ToList();

        await listAction.RequestListAsync(receiver, pageIndex: 1, CashShopStorageListAction.DefaultStorageType).ConfigureAwait(false);
        var purchasePackets = view.Collect();
        var purchase0E = purchasePackets.Where(p => p.Length == 244 && p[2] == 0xD2 && p[3] == 0x0E).ToList();
        var purchase0D = purchasePackets.Where(p => p.Length == 33 && p[2] == 0xD2 && p[3] == 0x0D).ToList();
        var purchaseCount = purchasePackets.Where(p => p.Length == 12 && p[2] == 0xD2 && p[3] == 0x06).ToList();

        Console.WriteLine($"Aba G: D2 0E={giftLines.Count} | D2 0D={gift0D.Count} (esperado 1/0)");
        Console.WriteLine($"Aba S: D2 0E={purchase0E.Count} | D2 0D={purchase0D.Count} (esperado 0/0 — nenhuma compra nesta execução) | D2 06={purchaseCount.Count}");

        var pass = true;
        if (giftLines.Count != 1 || gift0D.Count != 0)
        {
            Console.WriteLine("DESVIO: a aba de presentes deve listar o legado por D2 0E e nunca por D2 0D.");
            pass = false;
        }

        if (giftLines.Count == 1)
        {
            var senderField = giftLines[0].AsSpan(33, 11).ToArray();
            var messageField = giftLines[0].AsSpan(44, 200).ToArray();
            var senderAllZero = senderField.All(b => b == 0);
            var messageAllZero = messageField.All(b => b == 0);
            Console.WriteLine($"Legado: campo de remetente todo zero = {senderAllZero} | campo de mensagem todo zero = {messageAllZero}");
            if (!senderAllZero || !messageAllZero)
            {
                Console.WriteLine("DESVIO: o presente legado deve sair com campos vazios (zerados), sem remetente inventado.");
                pass = false;
            }
        }

        if (purchase0E.Count != 0)
        {
            Console.WriteLine("DESVIO: a aba de compras emitiu D2 0E.");
            pass = false;
        }

        if (purchaseCount.Count != 1)
        {
            Console.WriteLine("DESVIO: a aba de compras deve receber exatamente um D2 06.");
            pass = false;
        }

        return pass ? 0 : 1;
    }

    /// <summary>
    /// Cenário J (ETAPA 11): Gift → Claim real pelo destinatário — mesmo ItemId, storage
    /// consumido, item no inventário, zero débito e zero nova WCoinTransaction.
    /// </summary>
    private static async Task<int> ScenarioGiftToClaimAsync(IPersistenceContextProvider provider, CashShopStorageClaimService claimService, CashShopGiftService giftService, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario J: Gift -> Claim (mesmo ItemId, sem segundo debito) ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        var result = await SendGiftAsync(giftService, gameContext, GiftMessagePtBr).ConfigureAwait(false);
        if (result is null || !result.Success)
        {
            Console.WriteLine($"DESVIO: o gift de preparação falhou: {result?.ErrorMessage}");
            return 1;
        }

        var rowBefore = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        if (rowBefore?.Item is null)
        {
            Console.WriteLine("DESVIO: a linha do presente não tem item.");
            return 1;
        }

        var itemIdBefore = ItemIdOf(rowBefore.Item)!.Value;
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        var receiverBalanceBefore = await GetBalanceAsync(provider, receiverAccountId).ConfigureAwait(false);
        var receiverTransactionsBefore = await GetTransactionCountAsync(provider, receiverAccountId).ConfigureAwait(false);
        Console.WriteLine($"ItemId antes: {itemIdBefore} | Durability (quantity) antes: {rowBefore.Item.Durability} | GiftSender: '{rowBefore.GiftSender}'");

        var receiver = await HomologationPlayer.CreateAsync(gameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
        if (receiver is null)
        {
            Console.Error.WriteLine("O jogador destinatário não pôde ser criado; rode o seed primeiro.");
            return 1;
        }

        var claimResult = await claimService.ClaimAsync(receiver, rowBefore.StorageIndex, rowBefore.ItemSeq, (byte)'P').ConfigureAwait(false);

        var rowAfter = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        var inventoryItem = receiver.Inventory?.Items.FirstOrDefault();
        var receiverBalanceAfter = await GetBalanceAsync(provider, receiverAccountId).ConfigureAwait(false);
        var receiverTransactionsAfter = await GetTransactionCountAsync(provider, receiverAccountId).ConfigureAwait(false);

        Console.WriteLine($"Claim: success={claimResult.Success} errorCode={claimResult.ErrorCode}");
        Console.WriteLine($"Storage do presente depois: {(rowAfter is null ? "consumido" : "ainda presente")}");
        Console.WriteLine($"Item no inventário: {(inventoryItem is null ? "nenhum" : $"Id={ItemIdOf(inventoryItem)} Durability={inventoryItem.Durability}")}");
        Console.WriteLine($"WCoin do destinatario antes: {receiverBalanceBefore} | depois: {receiverBalanceAfter} | WCoinTransaction delta: {receiverTransactionsAfter - receiverTransactionsBefore}");

        var pass = true;
        if (!claimResult.Success)
        {
            Console.WriteLine("DESVIO: o claim do presente falhou.");
            pass = false;
        }

        if (rowAfter is not null)
        {
            Console.WriteLine("DESVIO: a linha do presente não foi consumida.");
            pass = false;
        }

        if (inventoryItem is null || ItemIdOf(inventoryItem) != itemIdBefore)
        {
            Console.WriteLine($"DESVIO: o item do inventário não é o mesmo (esperado Id {itemIdBefore}).");
            pass = false;
        }

        if (receiverBalanceAfter != receiverBalanceBefore || receiverTransactionsAfter != receiverTransactionsBefore)
        {
            Console.WriteLine("DESVIO: o claim gerou débito ou movimentação de WCoin.");
            pass = false;
        }

        return pass ? 0 : 1;
    }

    /// <summary>
    /// Cenário K (ETAPA 12, parte 1): mensagem longa é persistida já truncada em fronteira
    /// UTF-8 (igual ao fio) e mensagem vazia persiste NULL — ambos com entrega normal.
    /// </summary>
    private static async Task<int> ScenarioNegativeMessagesAsync(IPersistenceContextProvider provider, CashShopGiftService giftService, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario K: mensagem longa (300 bytes UTF-8) e mensagem vazia ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        var longMessage = new string('á', 150);
        var result = await SendGiftAsync(giftService, gameContext, longMessage).ConfigureAwait(false);
        var rowLong = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        var expectedTruncated = new string('á', 99);
        Console.WriteLine($"Mensagem longa: enviada 150 'á' (300 bytes) | persistida: {(rowLong?.GiftMessage is null ? "null" : $"{rowLong.GiftMessage.Length} chars / {Encoding.UTF8.GetByteCount(rowLong.GiftMessage)} bytes")}");

        await ClearReceiverStorageAsync(provider).ConfigureAwait(false);
        var emptyResult = await SendGiftAsync(giftService, gameContext, string.Empty).ConfigureAwait(false);
        var rowEmpty = await GetGiftRowOfReceiverAsync(provider).ConfigureAwait(false);
        Console.WriteLine($"Mensagem vazia: gift aceito = {emptyResult?.Success} | GiftMessage persistida = {(rowEmpty?.GiftMessage is null ? "null" : $"'{rowEmpty.GiftMessage}'")}");

        var pass = true;
        if (rowLong?.GiftMessage != expectedTruncated)
        {
            Console.WriteLine($"DESVIO: esperado o truncamento em fronteira UTF-8 ({expectedTruncated.Length} chars), obtido diferente.");
            pass = false;
        }

        if (rowEmpty is null || rowEmpty.GiftMessage is not null)
        {
            Console.WriteLine("DESVIO: a mensagem vazia deveria persistir NULL.");
            pass = false;
        }

        return pass ? 0 : 1;
    }

    /// <summary>
    /// Cenário L (ETAPA 12, parte 2): presente de outra conta nunca aparece na listagem do
    /// destinatário — a linha sintética de outra conta não gera D2 0E para o jogador.
    /// </summary>
    private static async Task<int> ScenarioOtherAccountScopeAsync(IPersistenceContextProvider provider, CashShopStorageListAction listAction, CashShopGiftService giftService, GameContext gameContext)
    {
        Console.WriteLine();
        Console.WriteLine("=== Cenario L: presente de outra conta nao aparece na listagem ===");
        await ResetStateAsync(provider).ConfigureAwait(false);
        Guid syntheticAccountId;
        using (var context = provider.CreateNewContext())
        {
            var synthetic = context.CreateNew<Account>();
            synthetic.LoginName = "csforeign";
            synthetic.PasswordHash = BCrypt.Net.BCrypt.HashPassword("homologation");
            synthetic.SecurityCode = BCrypt.Net.BCrypt.HashPassword("homologation");
            synthetic.EMail = "homologation@localhost";
            synthetic.RegistrationDate = DateTime.UtcNow;
            synthetic.State = 0;
            synthetic.TimeZone = 0;
            synthetic.VaultPassword = string.Empty;
            await context.SaveChangesAsync().ConfigureAwait(false);
            syntheticAccountId = synthetic.Id;

            var foreignRow = context.CreateNew<CashShopStorageItem>();
            foreignRow.AccountId = syntheticAccountId;
            foreignRow.StorageIndex = 0;
            foreignRow.ItemSeq = 0;
            foreignRow.StorageGroup = 673;
            foreignRow.ProductSeq = 58;
            foreignRow.PriceSeq = 95;
            foreignRow.CashPoint = ForeignRowCashPoint;
            foreignRow.ItemType = CashShopStorageItemType.Product;
            foreignRow.IsGift = true;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        // E também um presente legítimo do destinatário, com CashPoint distinto.
        var giftResult = await SendGiftAsync(giftService, gameContext, GiftMessagePtBr).ConfigureAwait(false);
        if (giftResult is null || !giftResult.Success)
        {
            Console.WriteLine("DESVIO: o gift de preparação falhou.");
            await CleanupForeignAccountAsync(provider, syntheticAccountId).ConfigureAwait(false);
            return 1;
        }

        using var receiver = await CreateCapturingPlayerAsync(gameContext).ConfigureAwait(false);
        var view = receiver!.View;
        await listAction.RequestListAsync(receiver, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);
        var packets = view.Collect();
        var giftLines = packets.Where(p => p.Length == 244 && p[2] == 0xD2 && p[3] == 0x0E).ToList();
        var foreignLeaked = giftLines.Any(p => Math.Abs(BinaryPrimitives.ReadDoubleLittleEndian(p.AsSpan(24)) - ForeignRowCashPoint) < 0.0001);
        Console.WriteLine($"Linhas D2 0E capturadas: {giftLines.Count} (esperado 1, apenas do destinatario) | linha estrangeira vazou: {foreignLeaked}");

        var pass = !foreignLeaked && giftLines.Count == 1;
        if (!pass)
        {
            Console.WriteLine("DESVIO: a listagem expôs linha de outra conta.");
        }

        await CleanupForeignAccountAsync(provider, syntheticAccountId).ConfigureAwait(false);
        return pass ? 0 : 1;
    }

    /// <summary>Sends one real gift with the given message through the production service.</summary>
    private static async Task<PurchaseResult?> SendGiftAsync(CashShopGiftService giftService, GameContext gameContext, string message)
    {
        var player = await HomologationPlayer.CreateAsync(gameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            return null;
        }

        return await giftService.ProcessGiftAsync(
            player,
            GiftHomologation.GiftPackageProductSeq,
            GiftHomologation.GiftProductDisplaySeq,
            GiftHomologation.GiftPriceSeq,
            GiftHomologation.GiftItemCode,
            509,
            false,
            GiftHomologation.ReceiverCharacterName,
            message).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the receiver player with a view that forwards to the real packet serializers
    /// over an in-process connection, recording every packet written to the wire.
    /// </summary>
    private static async Task<CapturingPlayer?> CreateCapturingPlayerAsync(GameContext gameContext)
    {
        return await CapturingPlayer.CreateAsync(gameContext, GiftHomologation.ReceiverLoginName, GiftHomologation.ReceiverCharacterName).ConfigureAwait(false);
    }

    private static async Task<GameConfiguration?> LoadConfigurationAsync(IPersistenceContextProvider provider)
    {
        using var configurationContext = provider.CreateNewConfigurationContext();
        return (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
    }

    private static async Task ResetStateAsync(IPersistenceContextProvider provider)
    {
        await ClearReceiverStorageAsync(provider).ConfigureAwait(false);
        var senderAccountId = await GetAccountIdAsync(provider, Program.HomologationLoginName).ConfigureAwait(false);
        if (senderAccountId != Guid.Empty)
        {
            await SetBalanceAsync(provider, senderAccountId, Program.HomologationBalance).ConfigureAwait(false);
        }
    }

    private static async Task CleanupAsync(IPersistenceContextProvider provider)
    {
        await ClearReceiverStorageAsync(provider).ConfigureAwait(false);
        var senderAccountId = await GetAccountIdAsync(provider, Program.HomologationLoginName).ConfigureAwait(false);
        if (senderAccountId != Guid.Empty)
        {
            await SetBalanceAsync(provider, senderAccountId, Program.HomologationBalance).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine("Limpeza: storage do destinatario limpo e saldo do remetente restaurado (somente dados do runner).");
    }

    private static async Task CleanupForeignAccountAsync(IPersistenceContextProvider provider, Guid accountId)
    {
        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        foreach (var item in items.Where(item => item.AccountId == accountId).ToList())
        {
            await context.DeleteAsync(item).ConfigureAwait(false);
        }

        var account = await context.GetByIdAsync<Account>(accountId).ConfigureAwait(false);
        if (account is not null)
        {
            await context.DeleteAsync(account).ConfigureAwait(false);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task ClearReceiverStorageAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        if (receiverAccountId == Guid.Empty)
        {
            return;
        }

        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        foreach (var item in items.Where(item => item.AccountId == receiverAccountId).ToList())
        {
            await context.DeleteAsync(item).ConfigureAwait(false);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task<Guid> GetAccountIdAsync(IPersistenceContextProvider provider, string loginName)
    {
        using var context = provider.CreateNewContext();
        var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
        return accounts.FirstOrDefault(account => account.LoginName == loginName)?.Id ?? Guid.Empty;
    }

    private static async Task<long> GetBalanceAsync(IPersistenceContextProvider provider, Guid accountId)
    {
        using var context = provider.CreateNewContext();
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    private static async Task SetBalanceAsync(IPersistenceContextProvider provider, Guid accountId, long balance)
    {
        using var context = provider.CreateNewContext();
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        if (wallet is null)
        {
            return;
        }

        wallet.Balance = balance;
        wallet.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task<int> GetTransactionCountAsync(IPersistenceContextProvider provider, Guid accountId)
    {
        using var context = provider.CreateNewContext();
        var transactions = await context.GetAsync<WCoinTransaction>().ConfigureAwait(false);
        return transactions.Count(transaction => transaction.AccountId == accountId);
    }

    private static async Task<int> GetReceiverStorageCountAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        if (receiverAccountId == Guid.Empty)
        {
            return 0;
        }

        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return items.Count(item => item.AccountId == receiverAccountId);
    }

    private static async Task<CashShopStorageItem?> GetGiftRowOfReceiverAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetAccountIdAsync(provider, GiftHomologation.ReceiverLoginName).ConfigureAwait(false);
        if (receiverAccountId == Guid.Empty)
        {
            return null;
        }

        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return items.FirstOrDefault(item => item.AccountId == receiverAccountId && item.IsGift);
    }

    private static async Task<int> GetGiftPriceAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var packages = await context.GetAsync<CashShopPackage>().ConfigureAwait(false);
        return (int)(packages.FirstOrDefault(package => package.PackageProductSeq == GiftHomologation.GiftPackageProductSeq)?.Price ?? 0);
    }

    /// <summary>
    /// A player whose cash shop view forwards to the real packet serializers over an in-process
    /// connection, recording the bytes that would reach the client.
    /// </summary>
    internal sealed class CapturingPlayer : Player
    {
        /// <summary>Initializes a new capturing player.</summary>
        public CapturingPlayer(IGameContext gameContext)
            : base(gameContext)
        {
            this.View = new CapturingCashShopView();
        }

        /// <summary>Gets the capturing cash shop view.</summary>
        internal CapturingCashShopView View { get; }

        /// <inheritdoc />
        protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new SingleViewPlugInContainer(this.View);

        /// <summary>Creates a logged-in capturing player for the given account/character.</summary>
        public static async Task<CapturingPlayer?> CreateAsync(IGameContext gameContext, string loginName, string characterName)
        {
            var player = new CapturingPlayer(gameContext);
            await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
            await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
            await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);

            var account = (await player.PersistenceContext.GetAsync<Account>().ConfigureAwait(false))
                .FirstOrDefault(candidate => candidate.LoginName == loginName);
            var character = account?.Characters.FirstOrDefault(candidate => candidate.Name == characterName);
            if (account is null || character is null)
            {
                return null;
            }

            player.Account = account;
            await player.SetSelectedCharacterAsync(character).ConfigureAwait(false);
            return player;
        }

        /// <summary>Container serving only the capturing cash shop view.</summary>
        private sealed class SingleViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
        {
            private readonly CapturingCashShopView _view;

            public SingleViewPlugInContainer(CapturingCashShopView view) => this._view = view;

            /// <inheritdoc />
            public T? GetPlugIn<T>()
                where T : class, IViewPlugIn => this._view as T;
        }
    }

    /// <summary>
    /// A view that forwards to the very same production serializer methods the remote view
    /// plugin calls, over an in-process connection, recording the emitted packets.
    /// </summary>
    internal sealed class CapturingCashShopView : ICashShopViewPlugIn
    {
        private readonly SpyConnection _connection = new();

        /// <summary>Collects the packets written since the last call.</summary>
        public List<byte[]> Collect()
        {
            var packets = new List<byte[]>();
            byte[]? packet;
            while ((packet = this._connection.TryReadPacket()) is not null)
            {
                packets.Add(packet);
            }

            return packets;
        }

        /// <inheritdoc />
        public ValueTask ShowPointInfoAsync(double totalCash, double cashCredit, double cashPrepaid, double totalPoint, double totalMileage) =>
            this._connection.SendCashShopPointInfoAsync(0, totalCash, cashCredit, cashPrepaid, totalPoint, totalMileage);

        /// <inheritdoc />
        public ValueTask ShowOpenResultAsync(bool success) => this._connection.SendCashShopOpenResultAsync(success);

        /// <inheritdoc />
        public ValueTask ShowStorageCountAsync(ushort totalItemCount, ushort currentPageItemCount, ushort pageIndex, ushort totalPage) =>
            this._connection.SendCashShopStorageCountAsync(totalItemCount, currentPageItemCount, pageIndex, totalPage);

        /// <inheritdoc />
        public ValueTask ShowStorageItemAsync(int storageIndex, int itemSeq, int storageGroupCode, int productSeq, int priceSeq, double cashPoint, char itemType) =>
            this._connection.SendCashShopStorageItemAsync(storageIndex, itemSeq, storageGroupCode, productSeq, priceSeq, cashPoint, itemType);

        /// <inheritdoc />
        public ValueTask ShowGiftStorageItemAsync(int storageIndex, int itemSeq, int storageGroupCode, int productSeq, int priceSeq, double cashPoint, char itemType, string? giftSender, string? giftMessage) =>
            this._connection.SendCashShopGiftStorageItemAsync(storageIndex, itemSeq, storageGroupCode, productSeq, priceSeq, cashPoint, itemType, giftSender, giftMessage);

        /// <inheritdoc />
        public ValueTask ShowScriptVersionAsync(ushort saleZone, ushort year, ushort yearIdentify) =>
            this._connection.SendCashShopScriptVersionAsync(saleZone, year, yearIdentify);

        /// <inheritdoc />
        public ValueTask ShowBannerVersionAsync(ushort bannerZone, ushort year, ushort yearIdentify) =>
            this._connection.SendCashShopBannerVersionAsync(bannerZone, year, yearIdentify);

        /// <inheritdoc />
        public ValueTask ShowEventItemCountAsync(ushort itemCount) => this._connection.SendCashShopEventItemCountAsync(itemCount);

        /// <inheritdoc />
        public ValueTask ShowBuyResultAsync(bool success, int errorCode, string? deliveredItem) =>
            this._connection.SendCashShopBuyResultAsync(success ? (byte)0 : unchecked((byte)errorCode), 0);

        /// <inheritdoc />
        public ValueTask ShowStorageItemUseResultAsync(StorageClaimErrorCode errorCode) =>
            this._connection.SendCashShopStorageItemUseResultAsync(unchecked((byte)errorCode));
    }

    /// <summary>
    /// An in-process connection backed by a pipe pair: everything written to
    /// <see cref="Output"/> can be read back packet by packet.
    /// </summary>
    internal sealed class SpyConnection : IConnection
    {
        private readonly Pipe _pipe = new();

#pragma warning disable CS0067 // Required by the interface; never raised here.
        public event AsyncEventHandler<ReadOnlySequence<byte>>? PacketReceived;

        public event AsyncEventHandler? Disconnected;
#pragma warning restore CS0067

        public bool Connected => true;

        public EndPoint? EndPoint => null;

        public EndPoint? LocalEndPoint => null;

        public PipeWriter Output => this._pipe.Writer;

        public AsyncLock OutputLock { get; } = new();

        public Task BeginReceiveAsync() => Task.CompletedTask;

        public ValueTask DisconnectAsync() => default;

        public void Dispose() => this._pipe.Reader.Complete();

        /// <summary>
        /// Reads one complete packet from the pipe without blocking when nothing is buffered
        /// (every <c>SendAsync</c> flushes one complete packet, so try-read is sufficient).
        /// </summary>
        public byte[]? TryReadPacket()
        {
            if (!this._pipe.Reader.TryRead(out var result))
            {
                return null;
            }

            if (result.Buffer.Length == 0)
            {
                this._pipe.Reader.AdvanceTo(result.Buffer.End);
                return null;
            }

            var length = result.Buffer.FirstSpan[1];
            if (result.Buffer.Length < length)
            {
                // Safety net only: a SendAsync always flushes one complete packet.
                var more = this._pipe.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
                result = more;
                length = result.Buffer.FirstSpan[1];
                if (result.Buffer.Length < length)
                {
                    this._pipe.Reader.AdvanceTo(result.Buffer.End);
                    return null;
                }
            }

            var bytes = result.Buffer.Slice(0, length).ToArray();
            this._pipe.Reader.AdvanceTo(result.Buffer.Slice(0, length).End);
            return bytes;
        }
    }
}
