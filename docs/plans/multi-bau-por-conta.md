# Plano: Múltiplos Báus por Conta (/bau N)

> Status: **planejado, não iniciado** — criado em 2026-09-11.
> Escopo: adicionar múltiplos armazéns (báus) por conta no OpenMU, com seleção via comando `/bau N`, integrado ao sistema VIP/WCoin existente.

## Contexto atual (verificado no código)

- **1 baú por conta**: `Account.Vault` (`src\DataModel\Entities\Account.cs:126`), um único `ItemStorage` compartilhado por todos os personagens.
- **Tamanho**: 15 linhas × 8 colunas = 120 slots (`src\DataModel\InventoryConstants.cs:158-163`); `Account.IsVaultExtended` dobra p/ 240 — mas **nada no código ativa essa flag** (só admin/DB).
- **Abertura**: NPC nº 240 "Baz The Vault Keeper" (`NpcInitialization.cs:64-72`) → `TalkNpcAction.cs:99-104` cria `player.Vault = new Storage(size, Account.Vault)` lazy; ou chat `/openware` (`OpenWarehouseChatCommandPlugIn.cs:25`, desabilitado por padrão, já tem gate `MinimumVipLevel`).
- **Pacotes**: `0x24` ItemMove (Vault=kind 2), `0x81` VaultMoveMoney, `0x82` VaultClosed, `0x83/00-02` PIN (unlock/set/remove).
- **Client custom é open-source**: `C:\Dev\MuOpenSource\muonline\Client.Main\Controls\UI\Game\Vault\VaultControl.cs` — grid 8×15 próprio, re-renderiza quando o servidor reenvia a lista de itens (`RefreshVaultContent`, linha 1338, via evento `VaultItemsChanged`). **Itens do baú chegam como store list (`StoreKind.Normal`) via `ShowVaultPlugIn`** (`src\GameServer\RemoteView\Vault\ShowVaultPlugIn.cs:31-42`).
- **VIP atual** (`src\GameLogic\VipService.cs`): só multiplicador de drop (Bronze 1.1 / Silver 1.25 / Gold 1.5 / Platinum 2.0), comprado com WCoin via `/vip`.

## Decisão de arquitetura

Cada baú = **um `ItemStorage` separado** na conta (próprio → dinheiro Zen independente por baú).
Slots do protocolo são `byte` (0-119), então NÃO se pode particionar um storage único >255 slots; storage separado resolve.

- **Opção A (fase 1)**: só servidor. Client permanece 100% igual — troca de baú = servidor reenvia store list, client atualiza sozinho. Zero mudança de protocolo.
- **Opção B (fase 2)**: abas ◀ ▶ "BAÚ n/N" no `VaultControl` + sub-packet `0x83/03 SelectVault` (opcional; dá pra viver só com `/bau N`).

## Fase 1 — Servidor (≈ 2-3 dias)

### 1. Persistência
- [ ] Entidade nova `AccountVault { Guid Id, Guid AccountId, int Number, ItemStorage Storage }` em `src\DataModel\Entities\` (+ `Account.Vaults` collection, `Number` único por conta).
- [ ] Migração EF em `src\Persistence\EntityFramework\Migrations\`: cria tabela; **move `Account.VaultId` atual para baú nº 1** (insert `AccountVault(Number=1, StorageId=VaultId)` para contas com vault existente).
- [ ] `Account.IsVaultExtended` e `Account.VaultId`: manter por compatividade ou marcar `[Obsolete]` (decidir; recomendado: vault legado vira baú 1 e `VaultId` é removido em migração futura).
- [ ] Config: `GameConfiguration.MaxVaultsPerAccount` (default 1) em `src\DataModel\Configuration\GameConfiguration.cs`; seed default em `GameConfigurationInitializerBase.cs`.

### 2. Lógica de jogo
- [ ] `Player` (state): `int CurrentVaultNumber` (default 1) + `IStorage? Vault` continua, apontando pro storage do baú ativo.
- [ ] `TalkNpcAction.cs:99-104`: ao abrir NPC `VaultStorage`, montar `Storage` a partir de `Account.Vaults.First(v => v.Number == player.CurrentVaultNumber)` (criar lazy se não existir); tamanho = `InventoryConstants.WarehouseSize` (ignorar `IsVaultExtended` ou mantê-lo dobrando todos).
- [ ] `MoveItemAction.cs` (~197, ~414): já opera em `player.Vault` — validar que o limite de destino usa o storage ativo correto.
- [ ] Zen: `TryDepositVaultMoney`/`TryTakeVaultMoney` (`Player.cs:954+`) → money do `ItemStorage` do baú ativo (cada baú com próprio Money).
- [ ] `CloseNpcDialogAction.cs:28`: `player.Vault = null` ao fechar (manter); opcional resetar `CurrentVaultNumber` p/ último usado.
- [ ] **PIN (`VaultPassword`)**: manter global da conta protegendo todos os baús (sem mudança de protocolo; `MoveItemAction.cs:239-245` já bloqueia travado).

### 3. Comando de chat `/bau`
- [ ] Plugin novo `GameLogic\PlugIns\ChatCommands\WarehouseSelectionChatCommandPlugIn.cs` (padrão espelhando `OpenWarehouseChatCommandPlugIn`):
  - `/bau` → lista: "Baus disponiveis: 1-3 (atual: 2)"
  - `/bau N` → valida `1 <= N <= VaultsWithAccess(player)`; seta `player.CurrentVaultNumber = N`; com janela aberta → reenvia itens via `IShowVaultPlugIn.ShowVaultAsync()` (o client atualiza sozinho); sem janela → abre (reusar spawn de NPC temporário do `/openware`).
  - `/bau list` (opcional) → mostra itens count/espaço livre por baú.
- [ ] Mensagens de erro: baú inexistente, sem acesso (VIP insuficiente), numérico inválido.
- [ ] Registro em `PlugInConfiguration` default (enable por padrão) — ver `src\Startup\Program.cs` onde `/openware`/`/vip` são plugados.

### 4. Acesso configurável (regras "quem pode ter quantos")
- [ ] Interface `IVaultAccessProvider` (ex.: `int GetMaxVaults(Player/Account)`) com implementação default = `GameConfiguration.MaxVaultsPerAccount`.
- [ ] Implementação VIP: mapa tier→báus (ex. Bronze 2 / Silver 3 / Gold 4 / Platinum 5) lendo `VipService`/`AccountVip`. Integrar com `Stats.IsVip`.
- [ ] Opcional WCoin: comprar baú extra permanente (mesmo padrão transacional do `/vip` com estorno em falha — commits cc1c431/458938).

### 5. Validação / Segurança
- [ ] Servidor SEMPRE valida `N <= acesso efetivo` antes de trocar (anti-cheat; client não manda número).
- [ ] Personagens da mesma conta logados simultâneos: decidir semântica — recomendado: baú ativo é **por personagem** (`CurrentVaultNumber` no Player, storage compartilhado da conta é o mesmo de hoje).

## Fase 2 — Client (≈ 1-2 dias, opcional)

- [ ] `VaultControl.cs`: botões ◀ ▶ no header (região de `DrawModernHeader`, linha ~621) mostrando `BAÚ {n}/{max}`; clique envia `0x83/03 SelectVault {byte n}` (novo sub-packet — adicionar nos XMLs `ClientToServerPackets.xml`/`ServerToClientPackets.xml` do `src\Network` + handler grupo `0x83` `VaultLockGroupPlugIn.cs`).
- [ ] Servidor responde com store list + `VaultMoneyUpdate` + (novo) `VaultSelectionInfo { current, max }` p/ o client saber o máximo.
- [ ] Alternativa sem packet novo: botões injetam `/bau N` no chat local.

## Website / Painel admin

- [ ] `deploy\all-in-one-traefik\website`: aba "Baus" no painel `account.html` mostrando baús da conta (número, itens, Zen, tier VIP necessário); toggle manual p/ dar baú extra (espelha o padrão do VIP tab atual).
- [ ] Web admin OpenMU (`src\Web\Shared\Components\Form\ItemStorageField.razor`): listar/trocar `Vault Extension` → virar lista de baús.
- [ ] Lembrete (memória): edits no painel exigem `--build website` (porta 8081).

## Testes

- [ ] Unit/integration em `tests\`: migração preserva vault existente como baú 1; `/bau N` rejeita sem acesso; item move entre baú1↔inv↔baú2 correta; Zen por baú independente; PIN bloqueia todos os baús; personagens simultâneos não corrompem estado.
- [ ] Teste manual com client: abrir NPC → `/bau 2` → grid atualiza sem fechar janela.

## Riscos / Pegadinhas

| Risco | Mitigação |
|---|---|
| Slots byte (máx 255 por storage) | Storage separado por baú (decisão acima) |
| Dupla escrita no vault por 2 chars logados | Mesmo padrão já existe hoje com vault único; manter lock/transação EF por storage |
| `IsVaultExtended` órfão | Decidir aposentadoria explícita na migração |
| Website cria conta sem baú 1 | Criar `AccountVault(1)` lazy no primeiro open (já é o padrão `??=` atual) |
| Client antigo (sem custom) conectado | Opção A não muda protocolo → compatível; baú 1 = vault atual |

## Ordem de execução sugerida

1. Entidade + migração (baú 1 legado)
2. TalkNpc/MoveItem/Money apontando p/ storage do baú ativo
3. `/bau` server-side com acesso default=1 (comportamento idêntico ao atual com N=1)
4. Config `MaxVaultsPerAccount` + gate VIP
5. Compra WCoin (opcional)
6. Client ◀ ▶ (fase 2)
7. Painel website + admin
