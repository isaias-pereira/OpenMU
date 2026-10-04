// <copyright file="SelectWarehouseChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlayerActions;
using MUnique.OpenMU.GameLogic.Views.Vault;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Lets the player select which of the numbered vaults (báus) to use. Vault 1 is free;
/// vaults 2-7 must be purchased in the shop (/loja). VIP status does not grant vaults.
/// </summary>
[Guid("1E99D7D5-0413-46B0-9A8C-4FB1C371F652")]
[PlugIn]
[Display(Name = "Select Warehouse (Baus)", Description = "Permite escolher qual bau numerado do jogador usara. Baus 2-7 sao comprados na loja (/loja). Uso: /bau [numero]")]
[ChatCommandHelp(Command, "Selecciona ou lista os baus da conta. Uso: /bau [numero]", null)]
public class SelectWarehouseChatCommandPlugIn : ChatCommandPlugInBase<SelectWarehouseChatCommandPlugIn.Arguments>
{
    private const string Command = "/bau";

    private readonly TalkNpcAction _talkNpcAction = new();

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, Arguments arguments)
    {
        // The command may only be used in a safezone/city (same check used to disable attacking there).
        if (!player.IsAtSafezone())
        {
            await player.ShowBlueMessageAsync("O comando /bau so pode ser usado na safezone ou cidade.").ConfigureAwait(false);
            return;
        }

        if (player.Account is not { } account)
        {
            return;
        }

        var maxVaults = await VaultAccess.GetMaxVaultsAsync(player.PersistenceContext, account.Id).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(arguments.Number))
        {
            await this.ShowVaultListAsync(player, account, maxVaults).ConfigureAwait(false);
            return;
        }

        if (!int.TryParse(arguments.Number, out var requested) || requested < 1)
        {
            await player.ShowBlueMessageAsync($"Numero invalido. Use /bau entre 1 e {maxVaults}.").ConfigureAwait(false);
            return;
        }

        if (requested > maxVaults)
        {
            await player.ShowBlueMessageAsync(
                $"Voce so tem acesso a {maxVaults} bau(s). Compre mais baus na loja (/loja) para liberar acesso.")
                .ConfigureAwait(false);
            return;
        }

        // Check if the player actually has access to this specific vault (not just the max count)
        var hasAccess = await VaultAccess.HasVaultAccessAsync(player.PersistenceContext, account.Id, requested);
        if (!hasAccess)
        {
            await player.ShowBlueMessageAsync(
                $"Voce nao tem acesso ao bau {requested}. Compre este bau na loja (/loja) para liberar acesso.")
                .ConfigureAwait(false);
            return;
        }

        player.CurrentVaultNumber = requested;

        var isOpen = player.OpenedNpc?.Definition.NpcWindow == NpcWindow.VaultStorage;
        if (isOpen && player.PlayerState.CurrentState == PlayerState.NpcDialogOpened)
        {
            // The vault window is open: swap the runtime storage and let the client refresh the grid.
            var storage = await VaultAccess.GetOrCreateVaultStorageAsync(player).ConfigureAwait(false);
            player.Vault = VaultAccess.CreateRuntimeStorage(player, storage);
            await player.InvokeViewPlugInAsync<IShowVaultPlugIn>(p => p.ShowVaultAsync()).ConfigureAwait(false);
            return;
        }

        if (player.CurrentMap is not { } currentMap)
        {
            return;
        }

        // Window closed: open the vault via a temporary vault-keeper, which lazily creates the storage.
        if (player.GameContext.Configuration.Monsters.FirstOrDefault(m => m.NpcWindow == NpcWindow.VaultStorage) is not { } definition)
        {
            await player.ShowBlueMessageAsync("Nenhum NPC de bau configurado.").ConfigureAwait(false);
            return;
        }

        var npc = new NonPlayerCharacter(new MonsterSpawnArea { MonsterDefinition = definition }, definition, currentMap);
        await this._talkNpcAction.TalkToNpcAsync(player, npc).ConfigureAwait(false);
    }

    private async ValueTask ShowVaultListAsync(Player player, MUnique.OpenMU.DataModel.Entities.Account account, int maxVaults)
    {
        await player.ShowBlueMessageAsync($"=== BAUS (acesso: {maxVaults}) — atual: {player.CurrentVaultNumber} ===").ConfigureAwait(false);
        for (var number = 1; number <= maxVaults; number++)
        {
            var vault = account.Vaults?.FirstOrDefault(v => v.Number == number);
            var items = vault?.Storage?.Items?.Count ?? 0;
            var money = vault?.Storage?.Money ?? 0;
            var marker = number == player.CurrentVaultNumber ? " <-" : string.Empty;
            await player.ShowBlueMessageAsync($"/bau {number}  |  {items} itens  |  {money} Zen{marker}").ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync("Use /bau <numero> para trocar de bau.").ConfigureAwait(false);
    }

    /// <summary>
    /// Arguments for the select warehouse chat command.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Gets or sets the requested vault number (as text, validated here). When empty, the vaults are listed.
        /// </summary>
        [Argument("numero", false)]
        public string? Number { get; set; }
    }
}
