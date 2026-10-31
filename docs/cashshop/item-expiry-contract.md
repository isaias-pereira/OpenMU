# Contrato de Expiração de Itens Temporários — Cash Shop (Fase 10)

Status: **GATES C–I CONCLUÍDOS — EXPIRAÇÃO REAL IMPLEMENTADA E VALIDADA. Homologação visual Main.exe pendente.**
Data: 2026-10-23 · Branch: `checkpoint/cashshop-phase10-isolated` @ `9b0b68292` (+ alterações locais NÃO commitadas)

---

## 0. Estado da implementação (GATES C–G/I, 2026-10-23)

| Gate | Escopo | Evidência |
|---|---|---|
| **C** | `Item.ActivatedAtUtc/ExpiresAtUtc` (timestamptz NULL) + `CashShopStorageItem.DurationSeconds` (integer NULL); migration aditiva `20261022071625_AddCashShopItemExpiry` | Rehearsal em PostgreSQL efêmero EXCLUSIVO `openmu_expiry_val_7df21efb`: baseline até `20261011053051`, linhas legadas sintéticas, fingerprint md5 antes=depois (`7c3b7088…`/`849e3c0d…`), GUIDs preservados, 0 retroatividade, Down/Up idempotente |
| **C-RED** | 8 testes comportamentais (`CashShopItemExpiryTests`) | RED registrado: 5 FAIL / 3 PASS antes da implementação (compra/gift sem duração, claim sem ativação, duração negativa aceita) |
| **D** | Duração congelada no buy e no gift (`storageItem.DurationSeconds = selection.DurationSeconds`; permanente novo = 0, NULL = legado) | GREEN: 153/153 CashShop |
| **E** | Ativação no claim no MESMO commit da entrega; relógio UTC injetável (`Func<DateTime>?`); `ActivatedAtUtc ??=` (exatamente uma vez, sem renovação); duração < 0 → recusa fail-safe; 0/NULL → permanente sem datas | GREEN (determinístico com relógio fixo) |
| **F** | Enforcement central `ItemExpiryPolicy.IsExpired/IsTemporary`: expirado não é equipado/negociado/transferido/mesclado (`MoveItemAction`), não é usado (`ItemConsumeAction`), não é material (`BaseItemCraftingHandler.DoMixAsync`), não concede atributos/set-bonus (`ItemPowerUpFactory`); legado NULL intocado | RED 6 FAIL/7 PASS → GREEN 13/13 (`CashShopItemExpiryEnforcementTests`) |
| **G** | `ExpiredTemporaryItemService.SweepExpiredItemsAsync` — reconciliação offline (entrada no mundo, ANTES da lista de inventário), baú (TalkNpcAction) e ciclo de recuperação do GameContext (sem SQL por tick — verificação em memória; banco só quando algo expirou); idempotente; desequipamento automático remove power-ups/aparência | GREEN 5/5 (`CashShopExpiryCleanupTests`), incluindo re-log com item expirado persistido |
| **H** | Metadados viajam com a instância (trade de ativo preserva prazo); stack merge/unstack com item temporário bloqueado (perda de metadado); craft de expirado recusado sem consumir | Coberto nos testes F + guardas em `ItemStackAction` |
| **I** | Suítes completas + cenários em PostgreSQL efêmero | CashShop **153/153** · OpenMU.Tests **684/684** · Network.Packets **493/493** · Initialization.Tests **23+2 ign.** · runner `claim-repro` **0 desvios** (IBS 084 real, 2 ciclos Gift→Claim, slots 12/13 únicos) · `claim-fallback-perf` **0 desvios, 5/5, mediana 46 ms** (2AT preservado com as colunas novas) · **evidência PG**: itens reivindicados com `ActivatedAtUtc`/`ExpiresAtUtc` persistidos e delta **exatamente 86.400 s** |

Correção de ferramenta inclusa: `--keepafter` do `claim-repro` agora é honrado (a limpeza do `finally` era incondicional — bug pré-existente do runner).

Limitações honestas: (1) o cenário 604 800 s (Panda Ring 7d) foi validado em unidade (contrato do resolver + ativação pela linha) — o E2E em PG usou a variante 95 (86 400 s); o mecanismo é idêntico (duração vem da linha); (2) o gancho do timer de recuperação (GameContext) é exercitado indiretamente (o serviço é testado diretamente; o hook é uma chamada de 1 linha); (3) `StackItemsAsync`/`UnstackItemsAsync` recusam joias temporárias com log (sem mensagem localizada nova — falha silenciosa segura).

---

## 1. Classificação real das ofertas (GATE A1 — dados do banco de homologação)

Consultado em `data."CashShopProduct"` / `"CashShopPackageProduct"` (19/10):

| Produto | ItemCode | PackageSeq | ProductSeq | PriceSeq | DurationSeconds (unit 386) | Tipo | Mecanismo necessário |
|---|---|---|---|---|---|---|---|
| Wizard Ring | 6676 (13/20) | 35 | 58 | **95** | **86.400 (1 Day)** | equipável de buff (ring) | **expiração na instância do Item** |
| Wizard Ring | 6676 | 35 | 58 | **96** | **604.800 (7 Days)** | idem | idem |
| Panda Ring | 6732 (13/122) | 37 | 57 | **93** | **86.400 (1 Day)** | pet-ring | idem |
| Panda Ring | 6732 | 37 | 57 | **94** | **604.800 (7 Days)** | idem | idem |
| Panda Ring | 6732 | 37 | 380 (prod) | 449 | 259.200 (3 Days) + Quantity 1 (PropertySeq 9, unit 147) | idem | idem |

- PropertySeq **10** com UnitType **386** (segundos) = linha de duração (agregada pelo resolver; `DurationSeconds` já existe em `CashShopSelectionResult`).
- PropertySeq **37/53** (unit 151) = **tempo de campanha** (365 dias) — metadata, **não** duração de uso.
- PropertySeq **9** (unit 147) = quantidade (não confundir com duração).
- Sem linha 10 → **permanente** (`DurationSeconds = 0`).
- GiftFlag 184 em ambos → giftable.
- **Nenhum** dos produtos A-suportados exige MagicEffect: são entregues como itens de inventário → o mecanismo correto é **expiração na instância do Item** (colunas `ActivatedAtUtc`/`ExpiresAtUtc`). O caminho de buff (MagicEffect/`CMsgBoxIGSUseBuffConfirm`) fica registrado como alternativa futura para itens puramente de efeito, fora desta fase.

## 2. Estados e invariantes (GATE A2)

| Estado | Definição |
|---|---|
| Storage permanente | `CashShopStorageItem` com duração contratada 0/NULL |
| Storage temporário não ativado | duração contratada > 0 e `ItemId` ainda vinculado (não reclamado) |
| Item permanente | `Item` com `ActivatedAtUtc`/`ExpiresAtUtc` NULL |
| Item temporário ativo | `ActivatedAtUtc`/`ExpiresAtUtc` NOT NULL e `CurrentUtc < ExpiresAtUtc` |
| Item temporário expirado | `CurrentUtc >= ExpiresAtUtc` (não utilizável; removido/desativado pela limpeza) |
| Item legado | itens anteriores à migration: metadados NULL → **preservados como permanentes, sem retroatividade** |
| Item novo temporariamente inconsistente | ativação sem duração contratada ou duração ≤ 0 com `ActivatedAtUtc` NOT NULL → **recusado na criação** (nunca persistido) |

Invariantes: duração contratada persiste no Storage no momento da compra/gift; o Claim grava `ActivatedAtUtc`/`ExpiresAtUtc` no mesmo commit da entrega (exatamente uma vez, sem recálculo em retry); tempo absoluto UTC; mudanças de catálogo não alteram duração já contratada; transferências não prorrogam; legado não recebe datas retroativas; GUID sempre preservado.

## 3. Contrato de persistência (GATE A3 — proposta, sem migration executada)

- **`CashShopStorageItem`**: + `DurationSeconds int NULL` (auditoria da oferta contratada).
- **`Item`**: + `ActivatedAtUtc timestamptz NULL`, **`ExpiresAtUtc timestamptz NULL`** — prazo acompanha a instância em qualquer transferência.
- Nulabilidade: NULL = permanente/legado. Índice parcial em `ExpiresAtUtc WHERE NOT NULL`.
- Reconhecimento de item novo temporário inválido: `DurationSeconds <= 0` com ativação pretendida → recusa no Claim/buy (nunca persistido).
- Migration Up: `ADD COLUMN` apenas. **Down não é rollback seguro após itens temporários existirem** — documentado como in reversível; exigirá verificação de ausência de itens temporários antes do Down.
- Itens de Csrepro (`fc39a101…`, `553ea101…`, `dc43a101…`) → NULL → permanentes, preservados.

## 4. Baseline (GATE B1 — executado nesta rodada)

- Comando: `dotnet test …/MUnique.OpenMU.Tests.csproj --filter FullyQualifiedName~CashShop`
- **145/145 aprovados** (141 históricos + 4 novos), 0 falhas, 0 ignorados, 38 s · branch/HEAD conforme acima.

## 5. Testes criados (GATE B2/B3 — executáveis, `CashShopDurationContractTests.cs`)

| Teste | Requisito | Resultado |
|---|---|---|
| `ResolveAsync_OneDayOffer_Aggregates86400Seconds` | 1 Day = 86.400 s | **PASS** |
| `ResolveAsync_SevenDayOffer_Aggregates604800Seconds` | 7 Days = 604.800 s | **PASS** |
| `ResolveAsync_PermanentOffer_AggregatesZeroSeconds` | permanente = 0 | **PASS** |
| `ResolveAsync_ConflictingDurations_RefusesSelection` | duração inequívoca | **PASS** |

- **Não são RED**: a agregação de duração já está implementada no resolver (2AS) — estes testes cravam a semântica-fonte que alimentará a expiração.
- **RED dependente da migration (GATE C)**: persistência contratada no Storage, ativação no Claim, expiração/enforcement, legado — **documentados como não executáveis** enquanto `CashShopStorageItem`/`Item` não tiverem as colunas (criar os testes agora quebraria a compilação, o que não é RED funcional). Serão criados no GATE C/D/E imediatamente após a migration.

## 6. Gates C–I

Concluídos em 2026-10-23 — ver a **seção 0** (estado da implementação e evidências). A pendência restante da Fase 10 é a homologação visual no Main.exe (roteiro manual em `docs/cashshop/phase-10-gift-storage-final.md`); sem tag/push até lá.
