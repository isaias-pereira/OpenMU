// <copyright file="ClaimReproHomologation.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Controlled reproduction of the duplicated inventory items observed on Cshomol2 (round 2D):
/// runs the real Gift → Claim cycle TWICE against a <b>disposable</b> account, with fresh player
/// contexts per cycle (exactly like the gift-storage homologation runner), then reloads the
/// character through a fresh player — the same path the failing login walks — and reports
/// duplicated slots or duplicate item ids.
/// </summary>
/// <remarks>
/// The reproduction account (<c>csrepro</c>) and everything it owns is removed at the end, so
/// the homologation data of the real test characters is never touched. Items that leak out of
/// an <c>ItemStorage</c> (orphans with <c>ItemStorageId = NULL</c>) are not readable through the
/// data-model context, so the runner prints evidence only for the character the login path
/// loads; orphaned rows are compared on the PostgreSQL side before/after by the operator.
/// </remarks>
public static class ClaimReproHomologation
{
    /// <summary>LoginName is varchar(10): 7 characters keep a margin.</summary>
    public const string ReproLoginName = "csrepro";

    /// <summary>The reproduction character.</summary>
    public const string ReproCharacterName = "Csrepro";

    /// <summary>The synthetic message sent with the reproduction gifts.</summary>
    public const string ReproGiftMessage = "repro";

    /// <summary>Identity of a data-model item through <see cref="Persistence.IIdentifiable"/>.</summary>
    private static Guid? ItemIdOf(Item? item) => (item as Persistence.IIdentifiable)?.Id;

    /// <summary>Removes the disposable reproduction account and everything it owns.</summary>
    public static async Task<int> RunCleanupOnlyAsync()
    {
        var loggerFactory = new NullLoggerFactory();
        await new ConfigFileDatabaseConnectionStringProvider().InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var provider = new PersistenceContextProvider(loggerFactory, null);
        await CleanupReproAccountAsync(provider).ConfigureAwait(false);
        Console.WriteLine("Limpeza concluída.");
        return 0;
    }

    /// <summary>Identificadores de itens criados pelos ciclos de reprodução (deletados no fim).</summary>
    private static readonly List<Guid> DisposableItemIds = new();

    /// <summary>Runs two Gift → Claim cycles and reports inventory duplicates.</summary>
    /// <param name="keepAfter">When true, the reproduction account is NOT cleaned up at the end, so the operator can inspect the database before running <c>claim-repro-clean</c>.</param>
    /// <returns>0 when no duplication was observed; 1 when the defect reproduced.</returns>
    public static async Task<int> RunAsync(bool keepAfter = false)
    {
        var loggerFactory = new NullLoggerFactory();
        return await RunClaimCyclesAsync(loggerFactory, cycles: 2, keepAfter: keepAfter).ConfigureAwait(false);
    }

    /// <summary>
    /// Rodada 2AU: mede o fallback do claim em um banco efêmero — N ciclos de row-gift sintética
    /// (criada pelo "remetente" em contexto próprio, sem catálogo IBS) + claim pelo destinatário
    /// cujo contexto é criado DEPOIS do gift (não rastreia a row → força o caminho GetByIdAsync),
    /// com logs de console para capturar os step timings da instrumentação 2AS.
    /// </summary>
    /// <param name="cycles">Number of gift/legacy → claim cycles to run.</param>
    /// <param name="legacy">When true, the synthetic rows are created as non-gift ('S' tab) storage rows — the "storage legado" shape — instead of gift rows. The claim path itself does not read IsGift; the flag only exercises the other row shape end to end.</param>
    public static async Task<int> RunFallbackPerfAsync(int cycles, bool legacy = false)
    {
        var loggerFactory = new ConsoleLoggerFactory();
        await new ConfigFileDatabaseConnectionStringProvider().InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var provider = new PersistenceContextProvider(loggerFactory, null);
        var configuration = await LoadConfigurationAsync(provider).ConfigureAwait(false);
        if (configuration is null)
        {
            Console.Error.WriteLine("No GameConfiguration in the ephemeral database; run the seed step first.");
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

        var claimService = new CashShopStorageClaimService(
            loggerFactory.CreateLogger<CashShopStorageClaimService>(),
            new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance),
            () => provider.CreateNewContext());

        await EnsureReproCharacterAsync(provider, configuration).ConfigureAwait(false);
        using (var context = provider.CreateNewContext(configuration))
        {
            var reproAccount = (await context.GetAsync<Account>().ConfigureAwait(false)).First(a => a.LoginName == ReproLoginName);
            var failures = 0;
            var times = new List<long>();
            try
            {
                for (var cycle = 1; cycle <= cycles; cycle++)
                {
                    var storageIndex = cycle - 1;
                    var itemSeq = cycle - 1;
                    // "Remetente": contexto próprio cria a linha de gift com o item entregue
                    // (a Definition é carregada por Id NO contexto full do remetente — anexar o
                    // objeto da configuração carregada faria o contexto tentar reinserir o grafo
                    // de configuração, violando as PKs já existentes no banco efêmero).
                    //
                    // RC2/GATE A (RED): a definition usada é a MESMA dos claims lentos observados
                    // em produção (Wizard Ring = group 13, number 20, ItemCode 6676 — claims
                    // 90798174 e e6386271, 41,7 s e 40,1 s no fallbackGetById). O item 1x1
                    // genérico usado antes tem grafo trivial e NÃO reproduz a lentidão.
                    using (var senderContext = provider.CreateNewContext())
                    {
                        var senderAccount = (await senderContext.GetAsync<Account>().ConfigureAwait(false)).First(a => a.LoginName == Program.HomologationLoginName);
                        var wizardRingDefinition = configuration.Items.FirstOrDefault(d => d.Group == 13 && d.Number == 20);
                        var definitionId = ((MUnique.OpenMU.Persistence.IIdentifiable)(wizardRingDefinition ?? configuration.Items.FirstOrDefault(d => d.Width == 1 && d.Height == 1) ?? configuration.Items.First())).Id;
                        var definition = await senderContext.GetByIdAsync<MUnique.OpenMU.DataModel.Configuration.Items.ItemDefinition>(definitionId, CancellationToken.None).ConfigureAwait(false);
                        var row = senderContext.CreateNew<CashShopStorageItem>();
                        row.AccountId = reproAccount.Id;
                        row.StorageIndex = storageIndex;
                        row.ItemSeq = itemSeq;
                        row.StorageGroup = 673;
                        row.ProductSeq = 58;
                        row.PriceSeq = 95;
                        row.CashPoint = 100;
                        row.ItemType = CashShopStorageItemType.Product;
                        row.IsGift = !legacy;
                        if (!legacy)
                        {
                            row.GiftSender = Program.HomologationLoginName;
                            row.GiftMessage = "fallback-perf";
                        }
                        var item = senderContext.CreateNew<Item>();
                        item.Definition = definition;
                        item.Durability = 20;
                        row.Item = item;
                        await senderContext.SaveChangesAsync().ConfigureAwait(false);
                    }

                    // Destinatário: player FRESCO criado DEPOIS do gift — seu contexto não
                    // rastreia a row nem o item, disparando o fallback por chave primária.
                    var receiver = await HomologationPlayer.CreateAsync(gameContext, ReproLoginName, ReproCharacterName).ConfigureAwait(false);
                    if (receiver is null)
                    {
                        Console.Error.WriteLine("O jogador de reprodução não pôde ser criado.");
                        failures++;
                        continue;
                    }

                    var overall = System.Diagnostics.Stopwatch.StartNew();
                    var result = await claimService.ClaimAsync(receiver, storageIndex, itemSeq, (byte)'P').ConfigureAwait(false);
                    overall.Stop();
                    times.Add(overall.ElapsedMilliseconds);
                    Console.WriteLine($"CICLO {cycle}: success={result.Success} code={result.ErrorCode} total={overall.ElapsedMilliseconds} ms");
                    if (!result.Success)
                    {
                        failures++;
                    }
                }
            }
            finally
            {
                await CleanupReproAccountAsync(provider).ConfigureAwait(false);
            }

            if (times.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Fallback (GetByIdAsync): amostras={times.Count} min={times.Min()} ms mediana={times.OrderBy(t => t).ElementAt(times.Count / 2)} ms max={times.Max()} ms ===");
            }

            Console.WriteLine($"=== claim-fallback-perf: {failures} desvio(s) ===");
            return failures == 0 ? 0 : 1;
        }
    }

    /// <summary>
    /// Minimal console logger factory: prints Information+ to stdout so the claim-trace step
    /// timings of the 2AS instrumentation are visible without adding a logging package.
    /// </summary>
    private sealed class ConsoleLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => new ConsoleLogger();

        public void Dispose() { }

        private sealed class ConsoleLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!this.IsEnabled(logLevel))
                {
                    return;
                }

                Console.WriteLine(formatter(state, exception));
                if (exception is not null)
                {
                    Console.WriteLine(exception);
                }
            }
        }
    }

    /// <summary>Core of the claim reproduction: N cycles with the given logger factory.</summary>
    /// <param name="keepAfter">When true, the cleanup in <c>finally</c> is skipped so the operator can inspect the database (run <c>claim-repro-clean</c> afterwards).</param>
    private static async Task<int> RunClaimCyclesAsync(ILoggerFactory loggerFactory, int cycles, bool keepAfter = false)
    {
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
        var claimService = new CashShopStorageClaimService(loggerFactory.CreateLogger<CashShopStorageClaimService>(), new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance), () => provider.CreateNewContext());

        var failures = 0;
        try
        {
            await EnsureReproCharacterAsync(provider, configuration).ConfigureAwait(false);

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Ciclo {cycle}: Gift -> Claim (jogadores com contextos frescos) ===");
                var giftResult = await SendGiftAsync(giftService, gameContext).ConfigureAwait(false);
                Console.WriteLine($"Gift: success={giftResult?.Success} code={giftResult?.ErrorCode}");
                if (giftResult is null || !giftResult.Success)
                {
                    Console.WriteLine("FALHA: o gift de preparação não foi aceito.");
                    failures++;
                    continue;
                }

                var row = await GetFirstGiftRowAsync(provider).ConfigureAwait(false);
                if (row is null)
                {
                    Console.WriteLine("FALHA: nenhuma linha de presente encontrada.");
                    failures++;
                    continue;
                }

                Console.WriteLine($"Linha: StorageIndex={row.StorageIndex} ItemSeq={row.ItemSeq} ItemId={ItemIdOf(row.Item)} Durability={row.Item?.Durability}");
                if (row.Item is { } rowItem && rowItem.GetId() is { } rowItemId && rowItemId != Guid.Empty)
                {
                    DisposableItemIds.Add(rowItemId);
                }
                var receiver = await HomologationPlayer.CreateAsync(gameContext, ReproLoginName, ReproCharacterName).ConfigureAwait(false);
                if (receiver is null)
                {
                    Console.Error.WriteLine("O jogador de reprodução não pôde ser criado.");
                    failures++;
                    continue;
                }

                var claimResult = await claimService.ClaimAsync(receiver, row.StorageIndex, row.ItemSeq, (byte)'P').ConfigureAwait(false);
                Console.WriteLine($"Claim: success={claimResult.Success} code={claimResult.ErrorCode}");
                await PrintFreshInventoryAsync(gameContext).ConfigureAwait(false);
            }

            // Leitura final por UM PLAYER FRESCO — o mesmo caminho que o login roda quando o
            // personagem entra no mundo, e onde a Rodada 2D viu o ArgumentException.
            var inventory = await ReadFreshInventoryAsync(gameContext).ConfigureAwait(false);
            var distinctIds = inventory.Select(i => i.ItemId).Distinct().Count();
            var duplicateSlots = inventory.GroupBy(i => i.Slot).Where(g => g.Count() > 1).Select(g => $"{g.Key}x{g.Count()}").ToList();

            Console.WriteLine();
            Console.WriteLine("=== Resultado da reprodução ===");
            Console.WriteLine($"Itens no inventário: {inventory.Count} | ItemIds distintos: {distinctIds} | Slots: [{string.Join(", ", inventory.Select(i => i.Slot))}]");
            Console.WriteLine($"Slots duplicados: {(duplicateSlots.Count == 0 ? "nenhum" : string.Join(", ", duplicateSlots))}");

            if (inventory.Count != 2 || distinctIds != inventory.Count || duplicateSlots.Count > 0)
            {
                Console.WriteLine("DEFEITO REPRODUZIDO: dois ciclos Gift -> Claim devem deixar exatamente 2 itens, com Ids e slots únicos.");
                failures++;
            }
        }
        finally
        {
            if (keepAfter)
            {
                Console.WriteLine("keepafter: limpeza pulada — inspecionar o banco e rodar claim-repro-clean ao final.");
            }
            else
            {
                await CleanupReproAccountAsync(provider).ConfigureAwait(false);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== Reprodução claim: {failures} desvio(s) ===");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<GameConfiguration?> LoadConfigurationAsync(IPersistenceContextProvider provider)
    {
        using var configurationContext = provider.CreateNewConfigurationContext();
        return (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
    }

    private static async Task<PurchaseResult?> SendGiftAsync(CashShopGiftService giftService, GameContext gameContext)
    {
        var sender = await HomologationPlayer.CreateAsync(gameContext, Program.HomologationLoginName, Program.HomologationCharacterName).ConfigureAwait(false);
        if (sender is null)
        {
            return null;
        }

        return await giftService.ProcessGiftAsync(
            sender,
            GiftHomologation.GiftPackageProductSeq,
            GiftHomologation.GiftProductDisplaySeq,
            GiftHomologation.GiftPriceSeq,
            GiftHomologation.GiftItemCode,
            509,
            false,
            ReproCharacterName,
            ReproGiftMessage).ConfigureAwait(false);
    }

    private static async Task<CashShopStorageItem?> GetFirstGiftRowAsync(IPersistenceContextProvider provider)
    {
        var accountId = await GetAccountIdAsync(provider).ConfigureAwait(false);
        using var context = provider.CreateNewContext();
        var rows = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return rows.Where(r => r.AccountId == accountId && r.IsGift).OrderBy(r => r.StorageIndex).FirstOrDefault();
    }

    /// <summary>
    /// Reads the reproduction character's inventory through a FRESH player — the same path the
    /// login takes when a character enters the world, which is exactly where the round-2D
    /// duplication crashed the load.
    /// </summary>
    private static async Task<List<(Guid ItemId, int Slot, double Durability)>> ReadFreshInventoryAsync(GameContext gameContext)
    {
        var player = await HomologationPlayer.CreateAsync(gameContext, ReproLoginName, ReproCharacterName).ConfigureAwait(false);
        if (player is null)
        {
            return new List<(Guid, int, double)>();
        }

        return player.Inventory!.Items
            .Select(i => (ItemIdOf(i) ?? Guid.Empty, (int)i.ItemSlot, i.Durability))
            .OrderBy(t => t.Item2)
            .ToList();
    }

    private static async Task PrintFreshInventoryAsync(GameContext gameContext)
    {
        var inventory = await ReadFreshInventoryAsync(gameContext).ConfigureAwait(false);
        Console.WriteLine($"Inventário recarregado por player fresco: {inventory.Count} item(ns) — [{string.Join("; ", inventory.Select(i => $"slot {i.Slot} dur {i.Durability}"))}]");
    }

    private static async Task<Guid> GetAccountIdAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
        return accounts.FirstOrDefault(a => a.LoginName == ReproLoginName)?.Id ?? Guid.Empty;
    }

    /// <summary>Creates the disposable reproduction account, character and wallet, idempotently.</summary>
    private static async Task EnsureReproCharacterAsync(IPersistenceContextProvider provider, GameConfiguration configuration)
    {
        using var context = provider.CreateNewContext();
        var account = (await context.GetAsync<Account>().ConfigureAwait(false)).FirstOrDefault(a => a.LoginName == ReproLoginName);
        if (account is null)
        {
            account = context.CreateNew<Account>();
            account.LoginName = ReproLoginName;
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword("homologation");
            account.SecurityCode = BCrypt.Net.BCrypt.HashPassword("homologation");
            account.EMail = "homologation@localhost";
            account.RegistrationDate = DateTime.UtcNow;
            account.State = 0;
            account.TimeZone = 0;
            account.VaultPassword = string.Empty;
            account.IsVaultExtended = false;
            await context.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine($"Conta de reprodução criada: {account.LoginName} ({account.Id}).");
        }

        var wallet = await context.GetByIdAsync<WCoinWallet>(account.Id).ConfigureAwait(false);
        if (wallet is null)
        {
            wallet = context.CreateNew<WCoinWallet>();
            wallet.Id = account.Id;
            wallet.Balance = 0;
            wallet.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        using var classContext = provider.CreateNewPlayerContext(configuration);
        var characterClass = (await classContext.GetAsync<CharacterClass>().ConfigureAwait(false)).FirstOrDefault(c => !c.IsMasterClass);
        if (characterClass is null)
        {
            Console.Error.WriteLine("Nenhuma classe disponível; o personagem de reprodução não pode ser criado.");
            return;
        }

        using var characterContext = provider.CreateNewContext();
        characterContext.Attach(characterClass);
        var freshAccount = (await characterContext.GetAsync<Account>().ConfigureAwait(false)).FirstOrDefault(a => a.LoginName == ReproLoginName);
        if (freshAccount is null)
        {
            return;
        }

        if (freshAccount.Characters.Any(c => c.Name == ReproCharacterName))
        {
            Console.WriteLine($"Personagem de reprodução já existe: {ReproCharacterName}.");
            return;
        }

        var created = characterContext.CreateNew<Character>();
        created.Name = ReproCharacterName;
        created.CharacterSlot = 0;
        created.CreateDate = DateTime.UtcNow;
        created.Experience = 0;
        created.MasterExperience = 0;
        created.LevelUpPoints = 0;
        created.MasterLevelUpPoints = 0;
        created.PositionX = 0;
        created.PositionY = 0;
        created.PlayerKillCount = 0;
        created.StateRemainingSeconds = 0;
        created.CharacterStatus = CharacterStatus.Normal;
        created.Pose = CharacterPose.Standing;
        created.UsedFruitPoints = 0;
        created.UsedNegFruitPoints = 0;
        created.IsStoreOpened = true;
        created.CharacterClass = characterClass;
        freshAccount.Characters.Add(created);
        await characterContext.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Personagem de reprodução criado: {created.Name}.");
    }

    /// <summary>
    /// Removes ONLY the disposable data the reproduction created: the claimed gift items (by
    /// tracked id — the item delete cascades it out of the storage row link, the pattern the
    /// round-9 runner uses) and any remaining cash shop storage rows of the repro account. The
    /// account, character, wallet and position remain as a durable test fixture.
    /// </summary>
    private static async Task CleanupReproAccountAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();

        // 1) Itens criados pelos ciclos: remoção direta pelo Id rastreado; o delete do item
        //    derruba a linha de storage da qual o item era membro do agregado (Item→row cascade).
        if (DisposableItemIds.Count > 0)
        {
            var allItems = await context.GetAsync<Item>().ConfigureAwait(false);
            foreach (var itemId in DisposableItemIds)
            {
                var item = allItems.FirstOrDefault(candidate => candidate.GetId() == itemId);
                if (item is not null)
                {
                    await context.DeleteAsync(item).ConfigureAwait(false);
                }
            }
        }

        // 2) Linhas de storage remanescentes da conta de reprodução (de runs anteriores
        //    com --keepafter): remoção direta.
        var accountId = await GetAccountIdAsync(provider).ConfigureAwait(false);
        if (accountId != Guid.Empty)
        {
            var rows = (await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false))
                .Where(r => r.AccountId == accountId)
                .ToList();
            foreach (var row in rows)
            {
                if (row.Item is { } rowItem)
                {
                    await context.DeleteAsync(rowItem).ConfigureAwait(false);
                }
                else
                {
                    await context.DeleteAsync(row).ConfigureAwait(false);
                }
            }
        }

        // 3) Itens atualmente no INVENTÁRIO da conta: inclui restos de execuções anteriores
        //    com --keepafter (não rastreados por este processo). A navegação usa o storage
        //    do personagem (a mesma estrutura que crashou o login de Cshomol2).
        {
            var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
            var character = accounts.FirstOrDefault(a => a.LoginName == ReproLoginName)?.Characters.FirstOrDefault(c => c.Name == ReproCharacterName);
            if (character?.Inventory is { } inventory)
            {
                foreach (var item in inventory.Items.ToList())
                {
                    await context.DeleteAsync(item).ConfigureAwait(false);
                }
            }
        }

        var saved = await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Limpeza: repro removido (SaveChanges={saved}); conta/personagem preservados como fixture.");
        DisposableItemIds.Clear();
    }
}
