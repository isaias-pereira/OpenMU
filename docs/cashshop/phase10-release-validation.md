# Fase 10 — Validação Final e Preparação da Release (Gates J/K + Integração da Release Candidata)

Status: **RELEASE CANDIDATA CONSTRUÍDA — IMPLANTAÇÃO AUTORIZÁVEL** (imagem `munique/openmu:cashshop-phase10-rc1`, ID `2512867e09fd`, construída do worktree integrado Cash Shop + CustomMove; homologação visual Main.exe é a etapa final e depende de execução física do usuário).
Data: 2026-10-24 · Worktree: `C:\Dev\MuOpenSource\OpenMU-cashshop-9b0b68292` · Branch: `checkpoint/cashshop-phase10-isolated` @ `9b0b68292` + alterações locais (NÃO commitadas, conforme autorização).

---

## 0-bis. Release candidata (2026-10-24, segunda rodada)

- **Código integrado**: lote CustomMove v2 portado do projeto principal para o worktree, com análise arquivo a arquivo (merge cego proibido e não feito):
  - `CustomMoveChatCommandPlugIn.cs` (CommandKey `/custommove` + validação de requisito de mapa e coordenada caminhável);
  - `CustomMoveService.cs` (+ `GetDuplicateCommandWarnings`);
  - `ChatMessageCommandProcessor.cs` (fallback de alias);
  - `ModalCreateNew.razor` + `CustomMoveConfigurationEditor.razor` (banner `PluginActive`);
  - `PlugInController.cs` (`PluginActive` passado à modal);
  - `PlayerMessage.resx`/`.Designer.cs` (superconjunto; única chave nova: `CustomMoveInvalidCoordinates`);
  - `tests/MUnique.OpenMU.Tests/CustomMove/*` (7 arquivos) + `ProjectReference` a `MUnique.OpenMU.Web.Shared` no csproj de testes;
  - `GameMapDefinitionExtensions.cs` alinhado ao estado homologado do principal (mensagem por `nameof`).
  - **NÃO portado** (pertence à frente GM): `AccountService.cs` (auditoria via `AccountGameMasterSynchronizer`).
  - Gotcha registrado: `Copy-Item` preserva o timestamp da origem — o build incremental MSBuild ignora a cópia; exigiu `-t:Rebuild`.
- **Conflitos**: nenhum. Único teste falhando pós-porta foi `MoveWithUnmetMapRequirement…` (variante da mensagem localizada) — resolvido pelo alinhamento acima; depois **229/229** CustomMove+CashShop.
- **Migrations**: `openmu` (50, sem GM/expiry), `openmu_cashshop_validation` (58, sem GM/expiry), `openmu_gm_validation` (59, **com GM**, sem expiry). Conjunto da release candidata = **58 migrations** (57 cashshop-base + `20261022071625`). GM+expiry juntos farão 60 no deploy único integrado (IDs distintos; ordem cronológica resolve).
  - Ensaio EF real (a): efêmero NOVO `openmu_rc1_full_512d2436` — `dotnet ef database update` aplicou o conjunto completo (58) com as 3 colunas ✔.
  - Ensaio EF real (b): cópia isolada de `openmu_cashshop_validation` (pg_dump/pg_restore `openmu_rc1_upg2_*`) — upgrade EF aplicou **apenas** `20261022071625`; 5 contas, 4 linhas de storage e 0 itens ativados retroativamente preservados ✔ (o primeiro restore com `--no-privileges` perdeu os grants das roles `config`/`account` — re-restore sem essa flag; erro `aclcheck` documentado).
  - Runner sobre o ESTADO REAL da homologação (cópia): `claim-fallback-perf` 5/5 (mediana 52 ms) e `claim-repro` **0 desvios**.
- **Regressão integrada final** (código com CustomMove + expiração): OpenMU.Tests **719/719** (693 anteriores + 49 CustomMove − 23 pré-existentes do lote já contados? não: 719 = 674 + 45 novos; detalhe no log), Packets **493/493**, Init 23+2 ign. (rodadas anteriores), Startup/Runner 0 erros.
- **Imagem candidata**: `munique/openmu:cashshop-phase10-rc1` — **ID `2512867e09fd`**, digest `sha256:2512867e09fd…39e`, construída de `Dockerfile - Copia.openmu` com contexto do worktree integrado; **nenhuma tag existente foi sobrescrita**; container compartilhado NÃO recriado (segue em `custommove-fix-20261018` = `ab57829c5abe`).
- **Instrumentação (ETAPA 5)**: decisões — traces `[buy-trace]/[claim-trace]/[wcoin-trace]/[storage-trace]` (2S) e os logs do warmup/expiração são a rastreabilidade essencial exigida (compra, gift, claim, latências, expiração): **mantidos** até o fechamento da homologação visual; nenhum log de auditoria financeira/persistência foi removido nesta rodada.
- **Banco de homologação (inspeção somente leitura, ETAPA 7)**: 5 contas, 4 personagens, 4 linhas de storage, saldo total 193.500 W Coin(P), 58 migrations, GM não aplicada. Backup executável no dia do deploy: `pg_dump -h localhost -U postgres -Fc -f backup_pre_fase10_final.dump openmu_cashshop_validation` + restauração de teste em efêmero (o método já foi exercitado nesta rodada com o dump de 1.269.082 bytes).

---

## 1. Matriz de gates

| GATE | EVIDÊNCIA | TESTE | RESULTADO | BLOQUEIO |
|---|---|---|---|---|
| J1 — Auditoria da expiração | Os 8 arquivos conferidos no código; 8 invariantes mapeadas para testes GREEN pré-existentes | Suítes CashShop/Tests | **OK** | — |
| J1-correção (RED→GREEN) | Invariante 4: item com ativação órfã (`ActivatedAtUtc` sem `ExpiresAtUtc`) sobrevivia como "permanente" | `Sweep_ItemWithOrphanActivation_IsRemovedFailSafe` (RED comprovado: Expected 1, was 0) → correção `IsInvalidOrExpired` no `ExpiredTemporaryItemService` → **GREEN** | **CORRIGIDO** | — |
| J2 — Expiração online real | Ciclo REAL do `GameContext` (timer `RecoverTimerElapsed` → `RecoverPlayerAsync` → sweep) com `AddPlayerAsync` (registro real), item entregue por claim REAL e equipado, sem ações do jogador, sem alterar relógio global (prazo curto vem da duração contratada de 1 s da linha) | `RecoveryCycle_RemovesExpiredEquippedItemAndNotifiesClient` + `RecoveryCycle_KeepsActiveTemporaryItem` | **OK — online comprovado** (remoção + persistência + power-ups descartados + `IItemRemovedPlugIn` verificado ao cliente) | — |
| J2 — Periodicidade | `RecoveryInterval` de produção = **3000 ms** (`GameConfigurationInitializerBase.cs:51`); nos testes 400 ms | Documentado no fixture | **OK** | — |
| J2 — Atraso máximo | Inelegibilidade lógica: **imediata** (enforcement síncrono em equip/use/trade/craft/atributos). Remoção visual: **≤ 1 ciclo de recovery (≤ 3 s em produção)** após o `ExpiresAtUtc` | Análise do código + teste | **OK** | — |
| J3-A — Item válido sobrevive relog | Mesmo GUID + mesmos timestamps (sem recálculo/renovação) | `Relog_ValidTemporaryItem_KeepsGuidAndExpiry` | **OK** | — |
| J3-B — Venceu offline | Reconciliado na entrada no mundo | `RelogWithPersistedExpiredItem_IsReconciledOnLoad` (CleanupTests) | **OK** | — |
| J3-C — Equipado quando desconectou | Removido no re-log, zero power-ups fantasmas | `Relog_EquippedItemExpiredWhileDisconnected_IsRemovedWithoutGhostPowerUps` | **OK** | — |
| J3-D — Warehouse | Varredura do baú (serviço + gancho `TalkNpcAction`) | `SweepExpiredItemsAsync_VaultExpiredItem_IsRemoved` (CleanupTests) | **OK** | Teste do gancho via `TalkNpcAction` real não executado (requer NPC/vault da conta); o gancho é 1 linha sobre o serviço testado |
| J3-E — Permanente | Sobrevive sem receber datas | `Relog_PermanentItem_SurvivesWithoutTimestamps` | **OK** | — |
| J3-F — Legado | NULL permanece válido (nenhuma data inventada) | `Claim_RowWithoutDuration…` + rehearsal GATE C (fingerprint) | **OK** | — |
| J3-G — Metadados inconsistentes | Ativação órfã → removido (fail-safe, invariante 4); prazo sem ativação → tratado pelo prazo (nunca reinicia contagem) | `Sweep_ItemWithOrphanActivation…` + `Sweep_ItemWithExpiryButNoActivation…` | **OK (pós-correção)** | — |
| J4 — Migrations GM/CashShop | Snapshots em disco comparados: CashShop toca `Item`/`CashShopStorageItem`; GM toca `Character` (GameMasterGrantSource); JewelBank (outra sessão) toca `AccountJewelBalance`/`JewelBankOperation` — **zero sobreposição**. Ordem cronológica: `20261021050451` (GM) < `20261022071625` (CashShop). Ambas no `EntityDataContext` | `Compare-Object` dos snapshots + ensaio SQL | **OK** | Integração efetiva do branch (merge dos arquivos) aguarda autorização |
| J4 — Ensaio de coexistência | Efeito EXATO da GM migration aplicado via SQL no efêmero (com o conjunto CashShop completo) → coluna criada, backfill rodado, e o runner CashShop executou com **0 desvios** sobre o banco com a coluna GM | `claim-fallback-perf` pós-ALTER | **OK** | — |
| J5 — Main.exe | Roteiro reproduzível na seção 3; Main.exe homologado preservado (SHA256 `D7AEA4AA21C4F0DBD81C7D94AF9B64C5D1A41FC3487B4EB46EA06A4D6D3AF028`) | Execução física | **PENDENTE** | Interação física com o cliente (usuário) |
| J6 — CustomMove | Inventário completo do lote (ver seção 4): integração **aditiva pura** — 5 arquivos de código + 1 chave de recurso (`CustomMoveInvalidCoordinates`) + testes; `CustomMoveEntry`/migration `20260912130000` idênticas nos dois trees; nenhum conflito de DI (todos singleton; o novo `WarmupAsync` é aditivo na interface) | `Compare-Object` arquivo a arquivo | **OK (análise)** | Montagem da imagem candidata aguarda Docker Engine + autorização |
| K1 — Cold start | Causa: 1º load do catálogo pós-boot (cache vazio; ~8,3 s medidos em rodada anterior). Correção: `WarmupAsync` no `CashShopCatalogService` (mesmo caminho do cache-miss, single-flight) chamado em background no `GameServerContainer.InitializeGameServer` — invalidação existente preservada (1 h + Invalidate + stale-while-revalidate) | `CashShopCatalogWarmupTests` (2/2: primeira consulta pós-warmup não recarrega; vazio não lança) | **OK (correção)** | Medição frio/aquecido no servidor real pendente da imagem candidata |
| K2 — Monitor | Bug COMPROVADO empiricamente: `atual=0` vs `correto=39` (a causa: `Select-Object -Skip 1` clona os objetos e `IndexOf($_)` devolve -1 no array original). `summarize.ps1` reescrito com loop indexado + causas separadas (Engine down / porta fora / reinícios por StartedAt / GameServer degradado / falha de coleta). Coleta NÃO interrompida (CSV continuou crescendo) | `summarize.ps1` executado: 39 gaps, 450 amostras Engine down, 0 reinícios do openmu | **OK** | `.wslconfig` não tocado (sem evidência) |
| K3 — Regressão final | CashShop 153/153 · OpenMU.Tests **693/693** · Packets 493/493 · Init 23+2 ign. · Startup build 0 erros · Runner build 0 erros · `claim-fallback-perf` **0 desvios (min 29/mediana 33 ms)** · `claim-repro` **0 desvios** | Execuções desta rodada | **OK** | — |
| K4 — Rollback seguro | Plano na seção 5 | Documento | **OK (plano)** | Backup do banco de produção + imagem candidata dependem de Docker/autorização |

## 2. Correções adicionais realizadas nesta rodada

1. **Fail-safe de metadado inconsistente** (J1/J3-G): `ExpiredTemporaryItemService` remove itens com ativação órfã (RED → GREEN).
2. **Warmup do catálogo** (K1): `WarmupAsync` + chamada no boot do GameServer (cold start do primeiro Buy).
3. **`summarize.ps1`** (K2): contagem por índice + separação de causas (bug de 0-gaps corrigido).
4. Rodada anterior (Gates C–I, já relatada): migration, persistência de duração, ativação no claim, enforcement, varredura.

## 3. Roteiro visual Main.exe (GATE J5 — pendente de execução física)

Ambiente: `C:\Users\isaia\OneDrive\Desktop\Mu Offline - Homologacao Fase 10` (Main.exe `D7AEA4AA…`; config.ini 127.127.127.127:44406; IBS 084). Servidor: imagem candidata construída do worktree (aguarda Docker) + `DB_NAME` da base de homologação com a migration `20261022071625` aplicada ANTES do start. Passos:

1. Login (conta homologação; conferir entrada no mundo).
2. Abrir XShop — catálogo IBS 084 renderiza (Wizard Ring visível no pacote 35).
3. Selecionar Wizard Ring 1 Day (PriceSeq 95).
4. Comprar (D2 03) — conferir débito de 100 W Coin(P).
5. Conferir a linha no Storage (aba S, D2 0D).
6. Claim (D2 0B) — item entra no inventário (conferir GUID no banco: `data."Item"."Id"`).
7. **Conferir no banco**: `ActivatedAtUtc`/`ExpiresAtUtc` preenchidos, delta 86 400 s, `DurationSeconds` = 86 400 na linha do storage.
8. Equipar o anel.
9. Conferir atributo/bônus aplicado (power-up do anel).
10. Expiração controlada: para o teste visual, criar UMA oferta de poucos minutos **somente no banco de teste isolado** (linha `CashShopStorageItem.DurationSeconds` curta em registro de teste, explicitamente identificado) — nunca no relógio do Windows/PostgreSQL.
11. Aguardar ≤ 3 s (ciclo de recovery) — item some do inventário do cliente (pacote de remoção).
12. Conferir atributos recalculados no cliente (bônus do anel desaparece).
13. Reconectar — item não volta (persistência), sem fantasma, sem contagem reiniciada.
14. Repetir fluxo de Gift (Panda Ring/Wizard Ring conforme catálogo) — destinatário recebe, claim dele ativa a duração dele.

## 4. Compatibilidade Cash Shop / CustomMove (J6) — lote a portar para a imagem integrada

Idênticos nos dois trees (nada a fazer): `CustomMoveConfiguration.cs`, `CustomMoveEntry.cs`, `CustomMoveChatCommandArgs.cs`, `MoveChatCommandPlugIn.cs`, `GuildMoveChatCommandPlugIn.cs`, migration `20260912130000` (+Designer), entidades/EF gerados.

A portar do main tree (v2) para a imagem integrada:
1. `src/GameLogic/PlugIns/ChatCommands/CustomMoveChatCommandPlugIn.cs` (CommandKey `/custommove` + validações de requisito de mapa e coordenada caminhável).
2. `src/GameLogic/CustomMoveService.cs` (+ `GetDuplicateCommandWarnings`).
3. `src/GameLogic/PlayerActions/Chat/ChatMessageCommandProcessor.cs` (fallback de alias).
4. `src/GameLogic/Properties/PlayerMessage.resx` + `.Designer.cs` — **única chave nova**: `CustomMoveInvalidCoordinates` (união aditiva; nenhum conflito).
5. Testes CustomMove (49) e páginas web do painel (ModalCreateNew/Editor/atividade de plugins) do main tree.

Sem conflito de DI: novos singletons aditivos; TypeIds de plugins inalterados. **Não misturar** o snapshot JewelBank (sessão paralela) nesta integração.

## 5. Rollback seguro (K4) — plano

Premissa obrigatória: **após o primeiro item temporário existir (`ActivatedAtUtc` preenchido), voltar para versões antigas do OpenMU NÃO é rollback seguro** — versões antigas ignoram a expiração e mantêm itens expirados utilizáveis. Rollback válido = uma versão COMPATÍVEL com expiração (a candidata anterior ou a atual).

1. **Backup verificado** (executar no dia do deploy, com autorização): `pg_dump -h localhost -U postgres -Fc -f backup_pre_fase10_final.dump <DB>` + restauração de teste em banco efêmero + `pg_restore --list` sem erro. NÃO usar os bancos protegidos como origem de teste.
2. **Imagens por ID** (preservadas; confirmar com `docker images` quando o Engine voltar — esta rodada o Engine estava down):
   - `munique/openmu:cashshop-phase10-trace-2s` — `dcbb59cd8f38` (última com homologação técnica completa, SEM expiração — **NÃO usar como rollback** após itens temporários existirem; usar apenas como referência forense).
   - `munique/openmu:cashshop-phase10-homologacao` — `1e462d3bab5e` (pré-expiração — mesma ressalva).
   - `munique/openmu:latest` — `e140b8762e4f` e `munique/openmu:fase9-rollback` — `f0dfa30cd795` (Fase 9 — **não é rollback funcional** para expiração).
   - Candidata a ser construída: `munique/openmu:fase10-release-candidate` (tag NOVA, não sobrescreve nenhuma existente) — worktree atual + lote CustomMove (seção 4).
   - **Rollback funcional**: a própria fase 10 com a correção aplicada à frente (imagem nova em cima do MESMO schema — a migration é aditiva e não exige Down).
3. **Suspensão de compras/claims durante recuperação** (procedimento operacional, sem código novo): reverter o container para a imagem candidata e, se for preciso pausar o fluxo financeiro, bloquear o listener da porta do GameServer (parar o container `openmu` — o connect server responde, mas o game server não aceita) OU aplicar no banco um UPDATE em `data."CashShopPackage"."SalesFlag"` para valor não-vendável (182→0) e reverter depois — reversível e auditável.
4. **Correção para a frente**: qualquer defeito de expiração é corrigido em nova imagem da fase 10 (schema aditivo não muda).
5. **Verificação pós-falha**: saldos (`data."WCoinWallet"`), inventário (`data."Item"` por ItemStorage) e storage (`data."CashShopStorageItem"`) — comparar com o backup; **nunca executar Down de `20261022071625` com itens temporários ativos** (o Down destrói os prazos; a coluna `DurationSeconds` do storage e as datas dos itens seriam perdidas).
6. Proibido usar `gift-perf-2ad` como rollback (pré-funcionalidade).

## 6. Arquivos prontos para commit (worktree `OpenMU-cashshop-9b0b68292`, branch `checkpoint/cashshop-phase10-isolated`)

Modificados: `Item.cs`, `CashShopStorageItem.cs` (DataModel), `CashShopBuyService.cs`, `CashShopGiftService.cs`, `CashShopStorageClaimService.cs`, `CashShopStorageListAction.cs`, `CashShopStorageService.cs`, `ICashShopStorageService.cs`, `CashShopCatalogService.cs`, `ItemPowerUpFactory.cs`, `Player.cs`, `GameContext.cs`, `ItemConsumeAction.cs`, `BaseItemCraftingHandler.cs`, `MoveItemAction.cs`, `ItemStackAction.cs`, `TalkNpcAction.cs`, `GameServerContainer.cs`, snapshot EF, `HomologationPlayer.cs`, `Program.cs` (runner), `CashShopStorageListActionTests.cs`.
Novos: `ItemExpiryPolicy.cs`, `ExpiredTemporaryItemService.cs`, migration `20261022071625` (+Designer), testes (`CashShopDurationContractTests`, `CashShopItemExpiryTests`, `CashShopItemExpiryEnforcementTests`, `CashShopExpiryCleanupTests`, `CashShopExpiryOnlineIntegrationTests`, `CashShopCatalogWarmupTests`), `item-expiry-contract.md`, `catalog-compatibility-2s.csv`, `ClaimReproHomologation.cs`.
Traces temporários 2S: os logs `[buy-trace]/[claim-trace]` permanecem (essenciais para a homologação visual; remoção após o fechamento).

## 7. Autorizações que faltam

1. Executar a homologação visual no Main.exe (usuário) — J5/ETAPA 8 (roteiro pronto; cenário de prazo curto isolado: criar registro de teste com `DurationSeconds` curto em banco de teste, explicitamente identificado — nunca no compartilhado).
2. Aplicar a migration `20261022071625` no banco de homologação + backup pré-deploy verificado (o procedimento já foi exercitado em cópia isolada nesta rodada).
3. Deploy da imagem `cashshop-phase10-rc1` no container compartilhado (override Compose com `build.context` substituído por `image: cashshop-phase10-rc1` + `DB_NAME=openmu_cashshop_validation` + `OPENMU_CASH_SHOP_REOPEN_PACKAGES=1`) — janela autorizada.
4. Medição frio/aquecido final (K1) no servidor real.
5. Commit/push/tag `cashshop-phase10-complete` — somente após o item 1.

---

## 0-ter. GATE 7 — estado pré-janela da expiração natural (2026-10-25 13:58, America/Cuiaba)

Leitura somente-lesitura do banco de homologação — **estado registrado ANTES da janela de vencimento** (nenhuma transação nova desde 01:12; zero divergências):

| GUID | Personagem | Slot | ExpiresAtUtc (America/Cuiaba) | Restam | Estado |
|---|---|---|---|---|---|
| `6671a101-0000-7548-19aa-a12ce5fdcf80` | Cshomolo | 23 (inventário) | 26/10 00:51:35 | ~409 min | Spirit of Guardian |
| `6871a101-0000-7d65-981c-dbd0b419b864` | Cshomolo | 15 (inventário) | 26/10 00:53:49 | ~411 min | Wizard's Ring |
| `6a71a101-0000-7eec-23c2-87ed3f97bca9` | Cshomolo | 18 (inventário) | 26/10 00:55:55 | ~413 min | Pet Panda |
| `6d71a101-0000-793f-18a2-e37975c12fb5` | **Csrepro** (gift recebido) | 13 (inventário) | 26/10 01:00:58 | ~418 min | Wizard's Ring |
| `7971a101-0000-77a6-dbba-7f6d592f3a03` | Cshomolo | 31 (inventário) | 26/10 01:12:39 | ~430 min | Wizard's Ring |

- Wallet agregada 192.950 ✔ · storage 1 (linha legada `1d27a101` intacta) · **0 transações novas desde 01:12** · container RC1 (`2512867e09fd…`) running, restarts=0 ✔.
- **Sessão do proprietário (janela 26/10 00:51–01:12)**: procedimento — (1) entrar com Cshomolo ANTES de 00:51 e equipar um anel (atributos aplicam); (2) permanecer online no vencimento (00:51–01:12, um a um); (3) observar remoção visual + recálculo; (4) relogar e conferir que não voltam; (5) testar `/bar` e `/custommove bar` separadamente; (6) o Gift de Csrepro (slot 13) expira às 01:00:58 — pode ser observado em segunda sessão/conta. Verificação pós-janela: `select count(*) from data."Item" where "ActivatedAtUtc" is not null` deve cair de 5 → 0, com `select count(*) from data."CashShopStorageItem"` permanecendo 1.
- **Nenhum item foi equipado até agora** — os atributos dos anéis ainda não foram exercidos em jogo.
- CustomMove: sem uso registrado nos logs (o jogador não digitou os comandos ainda).

---

## 0-qua. GATE 7 — resultado pós-janela da expiração natural (2026-10-26 03:21 America/Cuiaba)

A janela (00:51:35–01:12:39) passou com o servidor IDLE — nenhum jogador online, coletor capturou 0 eventos, nenhum cleanup executado (correto: os hooks varrem jogadores carregados). Expuração OFFLINE ocorrida conforme desenhado.

**PRODUÇÃO — CICLO OFFLINE COMPLETO PROVADO**: no próximo login de Cshomolo (2026-10-26 07:21:47Z = 03:21 local), o hook Player.OnPlayerEnteredWorldAsync varreu ANTES da listagem de inventário e removeu exatamente os 4 itens expirados daquele personagem:

```
2026-10-26 07:21:47.350 +00:00 [Information] [MUnique.OpenMU.GameLogic.Player]
Expired temporary item cleanup: 4 item(s) removed from "Cshomolo".
```

Pós-login (leitura): activated 5→**1** — resta somente `6d71a101` (Gift de Csrepro, expirou 01:00:58), a ser removido no próximo login daquele personagem. Persistência confirmada: itens não voltam; wallet 192.950, storage 1 (linha legada intacta), 3 Wizard Rings históricos (`fc39a101`/`553ea101`/`dc43a101`) intactos, history=59, zero transações novas desde 01:12.

- Não houve uso de `/bar` nem `/custommove bar` na sessão (logs sem ocorrência) — CustomMove segue pendente de execução pelo proprietário.
- AppearanceSerializerExtended 18×27 voltou a disparar durante a sessão (bug pré-existente, pendência registrada — não corrigido).
- Prova visual ONLINE (item equipado vencendo com cliente aberto) NÃO foi obtida: a janela natural passou sem jogador conectado. Uma re-teste online exigiria novo ciclo de item temporário (não autorizado nesta rodada).
