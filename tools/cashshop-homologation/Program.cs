// <copyright file="Program.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.EntityFramework;
using MUnique.OpenMU.Persistence.Initialization.DataInitialization;

/// <summary>
/// Thin test harness that prepares the OpenMU homologation database. It contains no production
/// logic: the Cash Shop runs inside the normal OpenMU process, container and dependency injection,
/// and this tool is not needed for the server to start in production.
/// </summary>
/// <remarks>
/// <para>
/// Everything that is not specific to homologation is delegated to the production code, so there is
/// a single implementation of each rule:
/// </para>
/// <list type="bullet">
/// <item>the database is selected through <c>DB_NAME</c>, read by
/// <see cref="ConfigFileDatabaseConnectionStringProvider"/>, instead of editing the shipped
/// <c>ConnectionSettings.xml</c>;</item>
/// <item>the base data is created by the official 1.04d Season 6 initialization;</item>
/// <item>the catalog seed is performed by <see cref="CashShopCatalogStartupInitializer"/>, the very
/// same entry point the server uses, which itself uses <see cref="CashShopCatalogParser"/> and
/// <see cref="CashShopCatalogInitializer"/>;</item>
/// </list>
/// <para>
/// The tool deliberately does not parse the IBS files, does not build catalog entities and does not
/// implement price, availability, purchase, delivery or wallet rules. It only selects the database,
/// runs the official migrations and initialization, reopens the seeded packages when asked, and
/// creates the test account, character and WCoin wallet.
/// </para>
/// <para>
/// Usage:
/// <code>
/// $env:DB_NAME='openmu_cashshop_validation'
/// $env:OPENMU_CASH_SHOP_CATALOG_PATH='...\Data\InGameShopScript\512.2012.084'
/// dotnet run --project tools\cashshop-homologation
/// </code>
/// Every step is idempotent: running it twice updates the existing rows instead of duplicating them.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>
    /// The login name of the account created for the cash shop homologation.
    /// </summary>
    /// <remarks>
    /// Kept short on purpose: <c>Account.LoginName</c> is <c>varchar(10)</c> in the database, so a
    /// longer name is rejected by the server instead of being created.
    /// </remarks>
    public const string HomologationLoginName = "cshomolo";

    /// <summary>
    /// The password of the homologation account.
    /// </summary>
    /// <remarks>
    /// The value is stored hashed with BCrypt, exactly like the official test accounts do
    /// (<c>VersionSeasonSix.TestAccounts.AccountInitializerBase</c>). Storing the plain text here
    /// made <c>AccountRepository</c> fail with <c>BCrypt.Net.SaltParseException: Invalid salt
    /// version</c> as soon as the client tried to log in.
    /// </remarks>
    public const string HomologationPassword = "homologation";

    /// <summary>
    /// The security code of the homologation account, used as the vault password and to confirm
    /// sensitive operations. It is stored hashed with BCrypt as well.
    /// </summary>
    public const string HomologationSecurityCode = "homologation";

    /// <summary>
    /// The WCoin balance given to the homologation account on every run.
    /// </summary>
    public const long HomologationBalance = 100_000;

    /// <summary>
    /// The name of the character used by the cash shop homologation.
    /// </summary>
    public const string HomologationCharacterName = "Cshomolo";

    /// <summary>
    /// Environment variable that reopens the validity window of the seeded packages.
    /// </summary>
    /// <remarks>
    /// The real IBS files of the 1.04d Season 6E3 client describe a 2011-2016 campaign, so every
    /// package is expired today and <c>CashShopCatalogService</c> filters all of them out — a
    /// purchase would be rejected as "package unavailable" before it ever reaches the debit. Setting
    /// this to <c>1</c> shifts the seeded window around the current date so the flow can be
    /// exercised. It changes only the seeded data, never the production filter.
    /// </remarks>
    public const string ReopenPackagesVariableName = "OPENMU_CASH_SHOP_REOPEN_PACKAGES";

    /// <summary>
    /// Entry point of the homologation seeder.
    /// </summary>
    /// <param name="args">Unused.</param>
    /// <returns>0 when the catalog was seeded; 1 otherwise.</returns>
    public static async Task<int> Main(string[] args)
    {
        var database = Environment.GetEnvironmentVariable("DB_NAME");
        Console.WriteLine($"Target database: {database ?? "<from ConnectionSettings.xml>"}");

        await new ConfigFileDatabaseConnectionStringProvider().InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        // Homologation concern only: the catalog directory is resolved by the production code
        // (CashShopCatalogStartupInitializer reads OPENMU_CASH_SHOP_CATALOG_PATH itself). The tool
        // only checks that the path exists, so it fails with a clear message instead of letting the
        // server-side initializer fail later. It deliberately does not parse the IBS files here.
        var catalogDirectory = Environment.GetEnvironmentVariable(CashShopCatalogInitializer.CatalogPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(catalogDirectory))
        {
            if (!Directory.Exists(catalogDirectory))
            {
                Console.Error.WriteLine($"Catalog directory not found: {catalogDirectory}");
                return 1;
            }

            Console.WriteLine($"Using catalog: {catalogDirectory}");
        }
        else
        {
            Console.WriteLine($"{CashShopCatalogInitializer.CatalogPathEnvironmentVariable} is not set; the production initializer will skip the catalog seed.");
        }

        var loggerFactory = new NullLoggerFactory();

        // Homologation concern only: the official Season 6 initialization. It also saves the base
        // data before the catalog seed, so there is exactly one owner for the seed.
        await EnsureGameDataAsync().ConfigureAwait(false);

        // The catalog seed is delegated to the production entry point. The tool does not parse IBS
        // files, does not build the entities and does not know any price or availability rule.
        var provider = new PersistenceContextProvider(loggerFactory, null);
        await CashShopCatalogStartupInitializer.InitializeIfConfiguredAsync(provider, loggerFactory).ConfigureAwait(false);

        Console.WriteLine("Catalog seed completed.");
        await ReopenPackagesIfRequestedAsync().ConfigureAwait(false);
        await EnsureHomologationAccountAsync().ConfigureAwait(false);
        await EnsureHomologationCharacterAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Shifts the validity window of the seeded packages to the present, when the environment asks
    /// for it.
    /// </summary>
    /// <remarks>
    /// The real IBS campaign ended in 2016, so without this every package is filtered out by
    /// <c>CashShopCatalogService</c> and a purchase is rejected as unavailable. Only the seeded rows
    /// are touched; the service's own filter is not changed, so production behaviour is unaffected.
    /// </remarks>
    /// <returns>A task that completes when the packages have been reopened, if requested.</returns>
    private static async Task ReopenPackagesIfRequestedAsync()
    {
        if (Environment.GetEnvironmentVariable(ReopenPackagesVariableName) != "1")
        {
            return;
        }

        var provider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        using var context = provider.CreateNewContext();
        var now = DateTime.UtcNow;
        var packages = (await context.GetAsync<MUnique.OpenMU.DataModel.Entities.CashShopPackage>().ConfigureAwait(false)).ToList();
        foreach (var package in packages)
        {
            package.StartDate = now.AddDays(-1);
            package.EndDate = now.AddYears(5);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Reopened {packages.Count} packages between {now.AddDays(-1):yyyy-MM-dd} and {now.AddYears(5):yyyy-MM-dd}.");
    }

    /// <summary>
    /// Runs the official 1.04d Season 6E3 data initialization when the configuration is still empty.
    /// </summary>
    /// <remarks>
    /// The schema alone is not enough for a cash shop purchase: the purchase validates that the
    /// product's <c>InGamePackageID</c> resolves to a real item, and with an empty
    /// <c>config.ItemDefinition</c> every product is rejected before the debit. The initialization
    /// creates the items, classes, maps and NPCs the client expects.
    /// </remarks>
    /// <returns>A task that completes when the game data is in place.</returns>
    private static async Task EnsureGameDataAsync()
    {
        var provider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        using var context = provider.CreateNewConfigurationContext();
        // ItemDefinition and CharacterClass are configuration types served through a repository that
        // needs the current game configuration, so they are read through a player context.
        using var configurationContext = provider.CreateNewConfigurationContext();
        var configuration = (await configurationContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
        if (configuration is not null)
        {
            using var playerContext = provider.CreateNewPlayerContext(configuration);
            if ((await playerContext.GetAsync<MUnique.OpenMU.DataModel.Configuration.Items.ItemDefinition>().ConfigureAwait(false)).Any())
            {
                Console.WriteLine("Game data already present; skipping data initialization.");
                return;
            }
        }

        Console.WriteLine("No item definitions present; the 1.04d Season 6E3 data initialization is required.");
        var initialization = new MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.DataInitialization(
            new PersistenceContextProvider(new NullLoggerFactory(), null),
            new NullLoggerFactory());
        await initialization.CreateInitialDataAsync(1, false).ConfigureAwait(false);
        Console.WriteLine("Game data initialization completed.");
    }

    /// <summary>
    /// Creates the character used by the cash shop homologation.
    /// </summary>
    /// <remarks>
    /// The purchase refuses to run without a selected character, so an account alone cannot
    /// exercise the flow. The first character class of the initialized configuration is used, which
    /// keeps this free of any balance decision: the class is taken from the data the official
    /// initialization created, not chosen here.
    /// </remarks>
    /// <returns>A task that completes when the character exists.</returns>
    private static async Task EnsureHomologationCharacterAsync()
    {
        var provider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        using var configurationContext = provider.CreateNewConfigurationContext();
        // CharacterClass is also a configuration type read through a repository that needs the
        // current game configuration, so it is queried through a player context.
        using var configContext = provider.CreateNewConfigurationContext();
        var configuration = (await configContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).FirstOrDefault();
        if (configuration is null)
        {
            Console.WriteLine("No GameConfiguration present; the game data initialization must run first.");
            return;
        }

        using var classContext = provider.CreateNewPlayerContext(configuration);
        var characterClass = (await classContext.GetAsync<MUnique.OpenMU.DataModel.Configuration.CharacterClass>().ConfigureAwait(false))
            .FirstOrDefault(candidate => !candidate.IsMasterClass);
        if (characterClass is null)
        {
            Console.WriteLine("No character class available; the game data initialization must run first.");
            return;
        }

        // The character class is served by a repository backed by a no-tracking context, so the
        // instance is detached from this context. Entity Framework would therefore treat the whole
        // reachable graph (including the drop item groups of the class) as new and try to insert it
        // again, failing with "duplicate key value violates unique constraint
        // PK_DropItemGroupItemDefinition". Attaching marks that graph as already existing.
        using var context = provider.CreateNewContext();
        context.Attach(characterClass);
        var account = (await context.GetAsync<MUnique.OpenMU.DataModel.Entities.Account>().ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.LoginName == HomologationLoginName);
        if (account is null)
        {
            Console.WriteLine("The homologation account does not exist; run the account step first.");
            return;
        }

        var existing = account.Characters.FirstOrDefault(character => character.Name == HomologationCharacterName);
        if (existing is not null)
        {
            Console.WriteLine($"Using existing character {existing.Name} (class {characterClass.Name}).");
            return;
        }

        var created = context.CreateNew<MUnique.OpenMU.DataModel.Entities.Character>();
        created.Name = HomologationCharacterName;
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
        created.CharacterStatus = MUnique.OpenMU.DataModel.Entities.CharacterStatus.Normal;
        created.Pose = MUnique.OpenMU.DataModel.Entities.CharacterPose.Standing;
        created.UsedFruitPoints = 0;
        created.UsedNegFruitPoints = 0;
        created.IsStoreOpened = true;
        created.CharacterClass = characterClass;
        account.Characters.Add(created);
        await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Created character {created.Name} with class {characterClass.Name}.");
    }

    /// <summary>
    /// Checks whether a stored credential can be verified as a BCrypt hash.
    /// </summary>
    /// <param name="hash">The stored value.</param>
    /// <returns>
    /// <c>true</c> when the value is a BCrypt hash of the expected credential; otherwise <c>false</c>.
    /// </returns>
    private static bool IsUsableHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            // Verify throws SaltParseException when the value is not a BCrypt hash at all.
            return BCrypt.Net.BCrypt.Verify(HomologationPassword, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the account used for the cash shop homologation and gives it a WCoin balance.
    /// </summary>
    /// <remarks>
    /// A purchase cannot be exercised without an account that has a wallet: the debit reads
    /// <c>WCoinWallet</c> by the account id and returns "insufficient balance" when the row is
    /// missing. The balance is topped up on every run so the flow can be repeated.
    /// </remarks>
    /// <returns>A task that completes when the account and wallet are in place.</returns>
    private static async Task EnsureHomologationAccountAsync()
    {
        var provider = new PersistenceContextProvider(new NullLoggerFactory(), null);
        using var context = provider.CreateNewContext();
        var account = (await context.GetAsync<MUnique.OpenMU.DataModel.Entities.Account>().ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.LoginName == HomologationLoginName);
        if (account is null)
        {
            account = context.CreateNew<MUnique.OpenMU.DataModel.Entities.Account>();
            account.LoginName = HomologationLoginName;
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(HomologationPassword);
            account.SecurityCode = BCrypt.Net.BCrypt.HashPassword(HomologationSecurityCode);
            account.EMail = "homologation@localhost";
            account.RegistrationDate = DateTime.UtcNow;
            account.State = 0;
            account.TimeZone = 0;
            account.VaultPassword = string.Empty;
            account.IsVaultExtended = false;
            await context.SaveChangesAsync().ConfigureAwait(false);
            Console.WriteLine($"Created account {account.LoginName} ({account.Id}).");
        }
        else
        {
            // An account created by an older version of this tool stored the plain text, which
            // AccountRepository cannot verify. Re-hash whenever the stored value is not a usable
            // BCrypt hash, so the tool repairs an existing homologation account on its own.
            if (!IsUsableHash(account.PasswordHash) || !IsUsableHash(account.SecurityCode))
            {
                account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(HomologationPassword);
                account.SecurityCode = BCrypt.Net.BCrypt.HashPassword(HomologationSecurityCode);
                await context.SaveChangesAsync().ConfigureAwait(false);
                Console.WriteLine($"Re-hashed the credentials of {account.LoginName}.");
            }

            Console.WriteLine($"Using existing account {account.LoginName} ({account.Id}).");
        }

        var wallet = await context.GetByIdAsync<MUnique.OpenMU.DataModel.Entities.WCoinWallet>(account.Id).ConfigureAwait(false);
        if (wallet is null)
        {
            wallet = context.CreateNew<MUnique.OpenMU.DataModel.Entities.WCoinWallet>();
            wallet.Id = account.Id;
            wallet.Balance = HomologationBalance;
            wallet.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            wallet.Balance = HomologationBalance;
            wallet.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        Console.WriteLine($"Wallet balance set to {wallet.Balance}.");
    }
}
