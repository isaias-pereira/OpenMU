// <copyright file="GiftHomologation.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.Linq;
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
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Runs the real cash shop gift flow against the homologation PostgreSQL database and prints the
/// before/after evidence for every acceptance scenario.
/// </summary>
/// <remarks>
/// <para>
/// This is the same production path a connected client exercises (the <c>D2 04</c> handler calls
/// <see cref="CashShopGiftService.ProcessGiftAsync"/>): the real catalog of the database, the real
/// resolver, the real storage service, the real WCoin debit and the real EF unit of work. No rule
/// is reimplemented here; the tool only builds the game objects and captures the evidence.
/// </para>
/// <para>
/// The state is reset at the start of every run (the receiver's storage rows are removed and the
/// sender's wallet is topped up), so the runner is repeatable and every scenario starts from a
/// known state. Only homologation rows are touched; production data is never read or written by
/// this tool when <c>DB_NAME</c> points to the validation database.
/// </para>
/// </remarks>
public static class GiftHomologation
{
    /// <summary>LoginName is <c>varchar(10)</c>; 8 characters keep a margin.</summary>
    public const string ReceiverLoginName = "cshomol2";

    /// <summary>The character of the receiver account.</summary>
    public const string ReceiverCharacterName = "Cshomol2";

    /// <summary>The giftable package used by the scenarios: the real Wizard Ring offer.</summary>
    public const int GiftPackageProductSeq = 35;

    /// <summary>The category (product display sequence) of the gift package.</summary>
    public const int GiftProductDisplaySeq = 31;

    /// <summary>The 1-day price variant of the gift package (100 WCoinP).</summary>
    public const int GiftPriceSeq = 95;

    /// <summary>The item code of the offer; group 13, number 20 (Wizard's Ring).</summary>
    public const ushort GiftItemCode = 6676;

    /// <summary>A package with <c>GiftFlag = 185</c>, which the client does not allow to gift.</summary>
    public const int NonGiftablePackageProductSeq = 34;

    /// <summary>The category of the non-giftable package.</summary>
    public const int NonGiftableProductDisplaySeq = 24;

    /// <summary>The message sent with the gift, as the client does.</summary>
    public const string GiftText = "Happy birthday!";

    /// <summary>Runs every gift homologation scenario and prints the evidence.</summary>
    /// <returns>0 when every scenario passed; 1 otherwise.</returns>
    public static async Task<int> RunAsync()
    {
        var loggerFactory = new NullLoggerFactory();
        await new ConfigFileDatabaseConnectionStringProvider().InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var provider = new PersistenceContextProvider(loggerFactory, null);

        await EnsureReceiverAsync(provider).ConfigureAwait(false);

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

        var failures = 0;
        var senderAccountId = await GetSenderAccountIdAsync(provider).ConfigureAwait(false);

        // A - gift valido: estado limpo, player fresco (o contexto do player le a wallet real do banco).
        Console.WriteLine();
        Console.WriteLine("=== Cenario A: gift valido (pacote 35, GiftFlag 184, destinatario cshomol2) ===");
        await ResetScenarioStateAsync(provider).ConfigureAwait(false);
        var before = await CaptureAsync(provider, senderAccountId).ConfigureAwait(false);
        var result = await RunGiftAsync(giftService, gameContext, provider, GiftPackageProductSeq, GiftProductDisplaySeq, ReceiverCharacterName).ConfigureAwait(false);
        var after = await CaptureAsync(provider, senderAccountId).ConfigureAwait(false);
        PrintEvidence(before, after);
        Console.WriteLine($"Resultado: {(result is null ? "falha ao criar o player" : $"{result.ErrorCode} - {result.ErrorMessage}")}");
        if (result is null || !result.Success || after.ReceiverStorageCount != before.ReceiverStorageCount + 1 || after.SenderBalance != before.SenderBalance - before.GiftPrice)
        {
            failures++;
        }

        // B - saldo insuficiente: o saldo e escrito no banco ANTES do player ser criado, entao o
        // contexto do player le o saldo real (99), e nao um valor em cache de um cenario anterior.
        Console.WriteLine();
        Console.WriteLine("=== Cenario B: saldo insuficiente ===");
        await SetBalanceAsync(provider, senderAccountId, 99).ConfigureAwait(false);
        var balanceBefore = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        var storageBefore = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        result = await RunGiftAsync(giftService, gameContext, provider, GiftPackageProductSeq, GiftProductDisplaySeq, ReceiverCharacterName).ConfigureAwait(false);
        var balanceAfter = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        var storageAfter = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        Console.WriteLine($"Saldo antes: {balanceBefore} | preco: {before.GiftPrice} | saldo depois: {balanceAfter}");
        Console.WriteLine($"Storage do destinatario antes: {storageBefore} | depois: {storageAfter}");
        Console.WriteLine($"Resultado: {(result is null ? "falha ao criar o player" : $"{result.ErrorCode} - {result.ErrorMessage}")}");
        if (result is null || result.Success || balanceAfter != balanceBefore || storageAfter != storageBefore)
        {
            failures++;
        }

        await SetBalanceAsync(provider, senderAccountId, Program.HomologationBalance).ConfigureAwait(false);

        // C - destinatario invalido
        Console.WriteLine();
        Console.WriteLine("=== Cenario C: destinatario invalido ===");
        balanceBefore = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        storageBefore = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        result = await RunGiftAsync(giftService, gameContext, provider, GiftPackageProductSeq, GiftProductDisplaySeq, "NaoExiste").ConfigureAwait(false);
        balanceAfter = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        var storageCountAfter = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        Console.WriteLine($"Saldo antes: {balanceBefore} | saldo depois: {balanceAfter}");
        Console.WriteLine($"Storage do destinatario antes: {storageBefore} | depois: {storageCountAfter}");
        Console.WriteLine($"Resultado: {(result is null ? "falha ao criar o player" : $"{result.ErrorCode} - {result.ErrorMessage}")}");
        if (result is null || result.Success || balanceAfter != balanceBefore || storageCountAfter != storageBefore)
        {
            failures++;
        }

        // D - pacote nao giftable (GiftFlag 185). O pacote real 34 e da moeda W Coin(C) (508), entao
        // a recusa vem do gate de moeda, que roda antes do gate de GiftFlag; o resultado observable
        // e o mesmo: gift recusado, WCoin nao debitado. O gate de GiftFlag em si e validado pelos
        // testes unitarios (0/1/182 recusados, 184 permitido).
        Console.WriteLine();
        Console.WriteLine("=== Cenario D: pacote nao giftable (pacote 34, GiftFlag 185, moeda W Coin(C)) ===");
        balanceBefore = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        result = await RunGiftAsync(giftService, gameContext, provider, NonGiftablePackageProductSeq, NonGiftableProductDisplaySeq, ReceiverCharacterName).ConfigureAwait(false);
        balanceAfter = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        Console.WriteLine($"Saldo antes: {balanceBefore} | saldo depois: {balanceAfter}");
        Console.WriteLine($"Resultado: {(result is null ? "falha ao criar o player" : $"{result.ErrorCode} - {result.ErrorMessage}")}");
        if (result is null || result.Success || balanceAfter != balanceBefore)
        {
            failures++;
        }

        // E - entrega falha: a aba de presentes do destinatario cheia, recusa sem debito.
        Console.WriteLine();
        Console.WriteLine("=== Cenario E: aba de presentes do destinatario cheia (recusa sem debito) ===");
        balanceBefore = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        await FillReceiverGiftStorageAsync(provider).ConfigureAwait(false);
        var filledCount = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false);
        result = await RunGiftAsync(giftService, gameContext, provider, GiftPackageProductSeq, GiftProductDisplaySeq, ReceiverCharacterName).ConfigureAwait(false);
        balanceAfter = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false);
        Console.WriteLine($"Aba de presentes do destinatario preenchida com: {filledCount} itens (capacidade 30)");
        Console.WriteLine($"Saldo antes: {balanceBefore} | saldo depois: {balanceAfter}");
        Console.WriteLine($"Resultado: {(result is null ? "falha ao criar o player" : $"{result.ErrorCode} - {result.ErrorMessage}")}");
        if (result is null || result.Success || balanceAfter != balanceBefore)
        {
            failures++;
        }

        await ClearReceiverStorageAsync(provider).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Storage do destinatario limpo apos o cenario E.");
        Console.WriteLine($"=== Homologacao do gift: {failures} cenario(s) com desvio ===");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Creates the receiver account, character and wallet, idempotently. A gift to a character of
    /// the sender's own account is refused by design, so a second account is required.
    /// </summary>
    private static async Task EnsureReceiverAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var account = (await context.GetAsync<Account>().ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.LoginName == ReceiverLoginName);
        if (account is null)
        {
            account = context.CreateNew<Account>();
            account.LoginName = ReceiverLoginName;
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword("homologation");
            account.SecurityCode = BCrypt.Net.BCrypt.HashPassword("homologation");
            account.EMail = "homologation@localhost";
            account.RegistrationDate = DateTime.UtcNow;
            account.State = 0;
            account.TimeZone = 0;
            account.VaultPassword = string.Empty;
            account.IsVaultExtended = false;
            await context.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine($"Created receiver account {account.LoginName} ({account.Id}).");
        }
        else
        {
            Console.WriteLine($"Using existing receiver account {account.LoginName} ({account.Id}).");
        }

        var wallet = await context.GetByIdAsync<WCoinWallet>(account.Id).ConfigureAwait(false);
        if (wallet is null)
        {
            wallet = context.CreateNew<WCoinWallet>();
            wallet.Id = account.Id;
            wallet.Balance = 0;
            wallet.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine("Created receiver wallet.");
        }

        using var configurationContext = provider.CreateNewConfigurationContext();
        var configuration = (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
        if (configuration is null)
        {
            Console.WriteLine("No GameConfiguration present; the receiver character cannot be created yet.");
            return;
        }

        using var classContext = provider.CreateNewPlayerContext(configuration);
        var characterClass = (await classContext.GetAsync<CharacterClass>().ConfigureAwait(false))
            .FirstOrDefault(candidate => !candidate.IsMasterClass);
        if (characterClass is null)
        {
            Console.WriteLine("No character class available; the receiver character cannot be created yet.");
            return;
        }

        using var characterContext = provider.CreateNewContext();
        characterContext.Attach(characterClass);
        var freshAccount = (await characterContext.GetAsync<Account>().ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.LoginName == ReceiverLoginName);
        if (freshAccount is null)
        {
            return;
        }

        var existing = freshAccount.Characters.FirstOrDefault(candidate => candidate.Name == ReceiverCharacterName);
        if (existing is not null)
        {
            Console.WriteLine($"Using existing receiver character {existing.Name}.");
            return;
        }

        var created = characterContext.CreateNew<Character>();
        created.Name = ReceiverCharacterName;
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
        Console.WriteLine($"Created receiver character {created.Name}.");
    }

    /// <summary>
    /// Creates a fresh sender player and runs one gift. A fresh player per scenario is what makes
    /// the balance check read the wallet as it is in the database: the player's context is a unit
    /// of work that caches the wallet it debited, so a previous scenario's debit would otherwise
    /// still be visible to the next one.
    /// </summary>
    private static async Task<PurchaseResult?> RunGiftAsync(
        CashShopGiftService giftService,
        IGameContext gameContext,
        IPersistenceContextProvider provider,
        int packageProductSeq,
        int productDisplaySeq,
        string receiverName)
    {
        var player = await CreateSenderPlayerAsync(gameContext, provider).ConfigureAwait(false);
        if (player is null)
        {
            Console.Error.WriteLine($"The sender account {Program.HomologationLoginName} or its character does not exist; run the seed step first.");
            return null;
        }

        return await giftService.ProcessGiftAsync(
            player,
            packageProductSeq,
            productDisplaySeq,
            GiftPriceSeq,
            GiftItemCode,
            509,
            false,
            receiverName,
            GiftText).ConfigureAwait(false);
    }

    /// <summary>Captures the wallet, storage and transaction state of the scenario.</summary>
    private static async Task<ScenarioState> CaptureAsync(IPersistenceContextProvider provider, Guid senderAccountId)
    {
        var state = new ScenarioState
        {
            SenderBalance = await GetBalanceAsync(provider, senderAccountId).ConfigureAwait(false),
            ReceiverStorageCount = await GetReceiverStorageCountAsync(provider).ConfigureAwait(false),
        };
        var giftPackage = await GetGiftPackageAsync(provider).ConfigureAwait(false);
        state.GiftPrice = giftPackage?.Price ?? 0;
        return state;
    }

    private static void PrintEvidence(ScenarioState before, ScenarioState after)
    {
        Console.WriteLine($"Saldo antes: {before.SenderBalance} | preco: {before.GiftPrice} | saldo depois: {after.SenderBalance}");
        Console.WriteLine($"Storage do destinatario antes: {before.ReceiverStorageCount} | depois: {after.ReceiverStorageCount}");
    }

    private static async Task<CashShopPackage?> GetGiftPackageAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var packages = await context.GetAsync<CashShopPackage>().ConfigureAwait(false);
        return packages.FirstOrDefault(package => package.PackageProductSeq == GiftPackageProductSeq);
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

    private static async Task<int> GetReceiverStorageCountAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetReceiverAccountIdAsync(provider).ConfigureAwait(false);
        if (receiverAccountId == Guid.Empty)
        {
            return 0;
        }

        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return items.Count(item => item.AccountId == receiverAccountId);
    }

    /// <summary>
    /// Fills the receiver's gift tab up to the capacity, so the next reservation finds no free
    /// index. The rows carry <c>IsGift = true</c>, because the reservation competes per tab: only
    /// gift rows take the gift tab's index space.
    /// </summary>
    private static async Task FillReceiverGiftStorageAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetReceiverAccountIdAsync(provider).ConfigureAwait(false);
        if (receiverAccountId == Guid.Empty)
        {
            return;
        }

        using var context = provider.CreateNewContext();
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        var giftCount = items.Count(item => item.AccountId == receiverAccountId && item.IsGift);
        for (var index = giftCount; index < 30; index++)
        {
            var storageItem = context.CreateNew<CashShopStorageItem>();
            storageItem.AccountId = receiverAccountId;
            storageItem.StorageIndex = index;
            storageItem.IsGift = true;
            storageItem.ItemType = CashShopStorageItemType.Product;
            storageItem.ProductSeq = 0;
            storageItem.PriceSeq = 0;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task ClearReceiverStorageAsync(IPersistenceContextProvider provider)
    {
        var receiverAccountId = await GetReceiverAccountIdAsync(provider).ConfigureAwait(false);
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

    private static async Task<Guid> GetSenderAccountIdAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
        return accounts.FirstOrDefault(account => account.LoginName == Program.HomologationLoginName)?.Id ?? Guid.Empty;
    }

    private static async Task<Guid> GetReceiverAccountIdAsync(IPersistenceContextProvider provider)
    {
        using var context = provider.CreateNewContext();
        var accounts = await context.GetAsync<Account>().ConfigureAwait(false);
        return accounts.FirstOrDefault(account => account.LoginName == ReceiverLoginName)?.Id ?? Guid.Empty;
    }

    private static async Task ResetScenarioStateAsync(IPersistenceContextProvider provider)
    {
        await ClearReceiverStorageAsync(provider).ConfigureAwait(false);
        var senderAccountId = await GetSenderAccountIdAsync(provider).ConfigureAwait(false);
        await SetBalanceAsync(provider, senderAccountId, Program.HomologationBalance).ConfigureAwait(false);
        Console.WriteLine($"Estado reiniciado: storage do destinatario vazio, saldo do remetente = {Program.HomologationBalance}.");
    }

    private static async Task<GameConfiguration?> LoadConfigurationAsync(IPersistenceContextProvider provider)
    {
        using var configurationContext = provider.CreateNewConfigurationContext();
        return (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
    }

    private static async Task<Player?> CreateSenderPlayerAsync(IGameContext gameContext, IPersistenceContextProvider provider)
    {
        var player = new HomologationPlayer(gameContext);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.LoginScreen).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.Authenticated).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.CharacterSelection).ConfigureAwait(false);

        var account = (await player.PersistenceContext.GetAsync<Account>().ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.LoginName == Program.HomologationLoginName);
        var character = account?.Characters.FirstOrDefault(candidate => candidate.Name == Program.HomologationCharacterName);
        if (account is null || character is null)
        {
            return null;
        }

        player.Account = account;
        await player.SetSelectedCharacterAsync(character).ConfigureAwait(false);
        return player;
    }

    /// <summary>
    /// A player without any view plug-ins: the homologation runs without a connected client, so
    /// every view invocation is a no-op.
    /// </summary>
    private sealed class HomologationPlayer : Player
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HomologationPlayer"/> class.
        /// </summary>
        /// <param name="gameContext">The game context.</param>
        public HomologationPlayer(IGameContext gameContext)
            : base(gameContext)
        {
        }

        /// <inheritdoc />
        protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new EmptyViewPlugInContainer();
    }

    /// <summary>The empty view plug-in container of the homologation player.</summary>
    private sealed class EmptyViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
    {
        /// <inheritdoc />
        public T? GetPlugIn<T>()
            where T : class, IViewPlugIn => null;
    }

    /// <summary>The captured state of one scenario.</summary>
    private sealed record ScenarioState
    {
        /// <summary>Gets or sets the sender's wallet balance.</summary>
        public long SenderBalance { get; set; }

        /// <summary>Gets or sets the receiver's storage item count.</summary>
        public int ReceiverStorageCount { get; set; }

        /// <summary>Gets or sets the gift package's price.</summary>
        public int GiftPrice { get; set; }
    }
}
