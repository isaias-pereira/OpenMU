// <copyright file="HomologationPlayer.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tools.CashShopHomologation;

using System;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A player without any view plug-ins: the homologation runs without a connected client, so
/// every view invocation is a no-op. Shared by the gift and claim homologation flows.
/// </summary>
internal sealed class HomologationPlayer : Player
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HomologationPlayer"/> class.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public HomologationPlayer(IGameContext gameContext)
        : base(gameContext)
    {
    }

    /// <summary>Gets the persistence context of this player, for direct evidence reads.</summary>
    internal IPlayerContext DataContext => this.PersistenceContext;

    /// <inheritdoc />
    protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new EmptyViewPlugInContainer();

    /// <summary>The empty view plug-in container of the homologation player.</summary>
    private sealed class EmptyViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
    {
        /// <inheritdoc />
        public T? GetPlugIn<T>()
            where T : class, IViewPlugIn => null;
    }

    /// <summary>
    /// Creates a logged-in player bound to the account with the given login name, using the
    /// account's character of the given name.
    /// </summary>
    /// <remarks>
    /// The account MUST be loaded through <c>GetAccountByLoginNameAsync</c> — the same entry
    /// point the real login flow uses (<c>GetAccountByLoginNameAsync</c> in the player context
    /// routes through <c>AccountRepository</c> + <c>AccountJsonObjectLoader</c>, which loads
    /// characters WITH their items). A plain <c>GetAsync&lt;Account&gt;()</c> does not attach
    /// the character's stored items to the in-memory inventory (the Fase 10 Round 2F finding:
    /// it leaves <c>Inventory.Items</c> empty, so the claim grid reports slots free that are
    /// actually occupied, writing duplicates into the database).
    /// </remarks>
    /// <param name="gameContext">The game context.</param>
    /// <param name="loginName">The account's login name.</param>
    /// <param name="characterName">The character's name.</param>
    /// <returns>The player, or null when the account/character does not exist.</returns>
    public static async Task<HomologationPlayer?> CreateAsync(IGameContext gameContext, string loginName, string characterName)
    {
        var player = new HomologationPlayer(gameContext);
        await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);

        var account = await player.PersistenceContext
            .GetAccountByLoginNameAsync(loginName)
            .ConfigureAwait(false);
        var character = account?.Characters.FirstOrDefault(candidate => candidate.Name == characterName);
        if (account is null || character is null)
        {
            return null;
        }

        player.Account = account;
        await player.SetSelectedCharacterAsync(character).ConfigureAwait(false);
        return player;
    }
}
