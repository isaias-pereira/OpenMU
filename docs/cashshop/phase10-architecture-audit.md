# Fase 10 — Auditoria Arquitetural e Contrato Definitivo da Expiração Real

Data: 2026-10-24 · Worktree: `C:\Dev\MuOpenSource\OpenMU-cashshop-9b0b68292` · `checkpoint/cashshop-phase10-isolated` @ `9b0b68292` (+ alterações locais)
Status: **CONTRATO VALIDADO — PRONTO PARA IMPLEMENTAÇÃO RED/GREEN** — e, além do plano, a implementação RED/GREEN **já foi executada e validada** nesta mesma sessão (Gates C–I/J/K, release candidata `munique/openmu:cashshop-phase10-rc1`). Este relatório confronta o contrato com o estado real do código, arquivo e linha.

---

## 1. Código existente — mecanismos encontrados

| Mecanismo | Arquivo:linha | É expiração de item? | Reutilizado? |
|---|---|---|---|
| `TemporaryItem` | `src/GameLogic/TemporaryItem.cs:14` | **NÃO** — item NÃO-persistente (trade/NPC/movimentação em memória); `MakePersistent` cria um `Item` novo | Não — sem relação com prazo |
| `MagicEffect` | `src/GameLogic/MagicEffect.cs:16-47` (`_finishTimer` = Timer em memória) | Buff de efeito com duração runtime — **fonte de tempo em memória, proibida como verdade** (§2.3) | Não como fonte de verdade; caminho alternativo registrado para itens puramente de efeito (nenhum produto A-suportado exige) |
| `MagicEffectsList` | `src/GameLogic/MagicEffectsList.cs` | Lista de buffs com dispose no vencimento do timer | Não |
| `AccountVip.ExpiresAt` | `src/DataModel/Entities/AccountVip.cs:42` (`DateTime?` timestamptz) | Precedente de prazo absoluto persistido no modelo | Padrão de coluna copiado (`DateTime?` UTC) |
| `ItemDropItemGroup`/`ItemLevelBonusTable` | config | Não (config de drop) | — |

**Conclusão**: não existia mecanismo de expiração de item no OpenMU; o desenho implementado é o menor possível e não duplica nada.

## 2. Contrato confrontado com a implementação (arquivo:linha real)

| § Contrato | Implementação real | Evidência |
|---|---|---|
| 2.1 Início apenas no Claim | Ativação no bloco staged do claim, após `AddItemAsync`, no MESMO `SaveChangesAsync` da entrega; `ActivatedAtUtc ??= clock` (exatamente uma vez, sem recálculo em retry) | `CashShopStorageClaimService.cs:311-314` |
| 2.2 Duração do resolver | `DurationPropertySequences = {2,12,28,58,10}`, `SecondsUnitType = 386`, conflito→recusa (`CashShopSelectionResolver.cs:65,81,349,399`); congelada em buy (`CashShopBuyService.cs:218`) e gift (`CashShopGiftService.cs:224`); `ExpiresAtUtc = ActivatedAtUtc + TimeSpan.FromSeconds(d)` (sem overflow: `int` segundos + TimeSpan) | — |
| 2.2 permanente | Ausência de linha de duração no catálogo = indicação comercial de permanência (2720 produtos reais; ofertas permanentes não têm linha 10); conflito de duas durações → recusa; duração negativa no banco → recusa fail-safe (`ClaimService.cs:245`) | Decisão registrada |
| 2.3 Contagem contínua UTC | Timestamps persistidos são a verdade; `DateTime.UtcNow`/relógio injetável; nenhum timer em memória | `ItemExpiryPolicy.cs:40` |
| 2.4 Expiração imediata + limpeza | Enforcement central síncrono (inelegibilidade imediata) + varredura idempotente por ciclo de recovery (3000 ms produção) e reconciliação offline | `ItemExpiryPolicy.IsExpired` (linha 40); `ExpiredTemporaryItemService.cs:44` |
| 2.4 Pacotes ao cliente | `IItemRemovedPlugIn.RemoveItemAsync(slot)` + `IAppearanceChangedPlugIn`/power-ups via `InventoryStorage.EquippedItemsChanged` (mecanismo nativo de desequipamento) | `ExpiredTemporaryItemService.cs:96`; `InventoryStorage.cs:122-130` |
| 2.5 Persistência na instância | Colunas na instância (`data."Item"`, timestamptz NULL), não na `ItemDefinition`; GUID/origem/propriedades preservados | `Item.cs` (`ActivatedAtUtc`/`ExpiresAtUtc`), `CashShopStorageItem.cs` (`DurationSeconds`) |
| 2.6 Transferências | Prazo viaja com a instância; expirado bloqueado em TODOS os movimentos; stack merge/split com temporário bloqueado (nada prorroga); trade de ativo preserva prazo | `MoveItemAction.cs:65,329`; `ItemStackAction.cs:45,107` |
| 2.7 Legado | NULL = permanente, sem retroatividade; 3/3 Wizard Rings de Csrepro **presentes e intactos** na homologação (verificado nesta rodada; colunas ainda não aplicadas lá — upgrade aditivo) | psql: `wring=3`, `expiry_cols=0` |
| 2.8 Financeiro | Claim não debita (existente); expiração não toca WCoin; rollback de entrega/persistência inalterado | — |
| 3 — GameServer boot | Warmup do catálogo no boot (K1) | `GameServerContainer.cs:193` |
| 3 — Trade/craft/material | Craft recusa material expirado ANTES de consumir/cobrar | `BaseItemCraftingHandler.cs:37`; `ItemConsumeAction.cs:38`; `ItemPowerUpFactory.cs:48,108` |

## 3. Persistência (definitiva, já executada)

- `data."Item"`: `ActivatedAtUtc` + `ExpiresAtUtc` — `timestamp with time zone` NULL (aditiva).
- `data."CashShopStorageItem"`: `DurationSeconds` — `integer` NULL (0 = permanente novo; NULL = legado; >0 = contratado).
- Migration `20261022071625_AddCashShopItemExpiry` (Up = 3 `ADD COLUMN`; Down = drop, **proibido com itens ativos**). Índice parcial não criado (varredura é in-memory; sem consulta por prazo).
- Ensaio real: efêmero do zero (58 migrations), cópia da homologação com upgrade de exatamente 1 migration, 0 retroatividade, 3/3 anéis intactos.
- Rollback de versão: coluna extra ignorada por versões antigas, mas elas **ignoram a expiração** → não são rollback funcional pós-itens temporários.

## 4. Segurança e concorrência

- **Exactly-once**: `ActivatedAtUtc ??=` + `ExpiresAtUtc` recalculado apenas da ativação preservada (nunca renova).
- **Concorrência**: `AsyncLock` por conta cobre claims simultâneos in-process; **multi-instância é risco documentado** (não mitigado — a autoridade temporal é do servidor; o cliente nunca fornece timestamps).
- **Falha entre entrega e persistência**: single `SaveChangesAsync` (EF transacional) + rollback defensivo + `UnsafeContextRetirement`; teste de save-falho GREEN.
- **Expiração simultânea a uso/equip/trade**: o enforce é síncrono por `IsExpired` no momento da operação (a limpeza atrasada não reabre elegibilidade).
- **Falha/reinício durante a limpeza**: por-item try/catch isolado + idempotência (segunda passada remove 0); reinício não renova prazo (testado).
- **Auditoria de remoção**: log estruturado por item removido; **evolução opcional** (não implementada, decisão de menor arquitetura): tabela de auditoria de expiração caso o proprietário queira histórico consultável além dos logs.

## 5. Testes (RED/GREEN executados)

- **RED comprovado antes da implementação**: 5/8 do fixture `CashShopItemExpiryTests` (compra sem duração ×2, gift sem duração, claim sem ativação, duração negativa aceita) + 6/13 do fixture de enforcement (equipar/trade/atributos/consumir/craft/stack de expirado) + ativação órfã (J3-G). Nenhum assert alterado para GREEN.
- **GREEN final**: CashShop 153/153 → 229/229 integrado com CustomMove; OpenMU.Tests **719/719** (inclui `CashShopExpiryOnlineIntegrationTests` — ciclo REAL do GameContext com relógio do teste via duração de 1 s, sem Sleep de 24 h e sem tocar o relógio global; `CashShopExpiryCleanupTests` idempotência/relog; `CashShopExpiryEnforcementTests` 13/13; warmup 2/2); Packets 493/493; Init 23+2 ign.
- **PostgreSQL efêmero**: migration rehearsed (fingerprint + GUIDs + Down/Up) e upgrade de cópia da homologação com runner **0 desvios**.
- **Main.exe**: roteiro pronto (`phase10-release-validation.md` §3); **execução visual pendente** (usuário).

## 6. Implementação (estado real)

Arquivos e pontos: listados na seção 2. Ordem de gates executada: C (migration) → C-RED → D (duração no storage) → E (ativação no claim) → F (enforcement) → G (limpeza + cliente) → H (transferências/stacks) → I (regressão) → J (online/offline/rollback) → K (RC1). Riscos remanescentes: (a) multi-instância sem autoridade temporal compartilhada (documentado); (b) exclusão física na limpeza — rollback via backup verificado, sem tabela de auditoria (evolução opcional); (c) atraso de até 1 ciclo (≤3 s) entre `ExpiresAtUtc` e a remoção visual — inelegibilidade lógica é imediata.

## 7. Preservação

- **`openmu`**: intocado (histórico 50; fora do escopo da RC1).
- **`openmu_cashshop_validation`**: intocado nesta rodada (histórico 58; upgrade pendente = só a expiry; backup verificado `openmu_cashshop_validation_pre_rc1_20261024_200259.dump` SHA256 `7DC77306…23B`).
- **Wizard Rings de Csrepro**: `fc39a101…`, `553ea101…`, `dc43a101…` — **3/3 presentes, sem timestamps, nunca tocados**.
- **JewelBank**: não tocado (tabelas órfãs na homologação compreendidas; proibido `migrations add` do worktree contra esse banco).
- **CustomMove**: lote v2 integrado na RC1 sem conflito.
- **MuMain/Main.exe**: `D7AEA4AA…` preservado; roteiro visual pronto.
- **Docker**: container compartilhado intocado (segue `custommove-fix-20261018`/`ab57829c5abe`); monitor intacto; `summarize.ps1` corrigido (K2).
