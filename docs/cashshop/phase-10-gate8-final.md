# Fase 10 — Gate 8 / Homologação Final e Release Readiness

**Data:** 2026-10-30  
**Versão:** RC2 (`munique/openmu:cashshop-phase10-rc2`)  
**Branch:** `checkpoint/cashshop-phase10-isolated` (HEAD `9b0b68292`)  
**Ambiente:** Docker Compose ativo (`openmu`, `database`, `traefik`, `website`)  
**Banco de Homologação:** `openmu_cashshop_validation` (PostgreSQL nativo Windows, host.docker.internal:5432, 59 migrations)  
**Banco Docker (não usado pela RC2):** `openmu` (PostgreSQL 15 no container `database`, 57 migrations)  
**Cliente SkinFix:** `C:\Users\isaia\OneDrive\Desktop\Mu Offline - Homologacao Fase 10 - SkinFix\Main.exe` (SHA256: `DCE427CC6E82CC8218FE3C1F11F3570F3A454350478666873937A05AA5584766`)

---

## Sumário Executivo

| Gate | Status | Observação |
|------|--------|------------|
| **8A — Gift/Claim Real** | ✅ **PASS** | Gift enviado, Claim realizado, item entregue com expiração |
| **8B — CustomMove** | ✅ **PASS** | `/bar` e `/custommove bar` funcionando; sem conflito com `/move` nativo |
| **8C — Aparência Final** | ✅ **PASS** | Owner confirmou: Skeleton Ring + Panda Ring funcionando no SkinFix; sem invisibilidade; re-equip preserva modelo |
| **8D — Integridade/Regressão** | ✅ **PASS** | RC2 operacional; 59 migrations no banco correto; 0 WCoin/Storage sujos; 0 exceções |
| **8E — Release Prep** | ✅ **PRONTO** | Diffs mapeados; binários versionados; docs atualizadas; rollback checklist pronto |

**DECISÃO GERAL:** **FASE 10 HOMOLOGADA FUNCIONALMENTE — PRONTA PARA AUTORIZAÇÃO DE VERSIONAMENTO**

---

## Gate 8A — Gift/Claim Real: ✅ PASS

### Evidência da Execução Real

A operação Gift → Claim foi executada presencialmente pelo proprietário e confirmada por logs e banco de dados.

#### Dados da Transação

| Item | Valor |
|------|-------|
| **Correlation ID** | `1d8c84ea` |
| **Remetente** | `Cshomolo` (AccountId: `cbf1a001-0000-7c6e-fe53-af71e09f7deb`) |
| **Destinatário** | `Csrepro` (AccountId: `172ba101-0000-781a-d177-b58317a0a02e`) |
| **Item** | Wizard's Ring (Package 35, PriceSeq 95, CashPoint 100) |
| **DefinitionId** | `00000080-000d-0014-0000-000000000000` |
| **DurationSeconds** | 86400 (1 dia = 86400 segundos) |
| **Gift D2 04** | 2026-10-30 04:14:20 UTC |
| **Claim D2 0B** | 2026-10-30 04:15:01 UTC |
| **Correlation ID (Claim)** | `1d8c84ea` |

#### Reconciliação Financeira

| Conta | Antes | Depois | Delta | Status |
|-------|-------|--------|-------|--------|
| **Cshomolo (remetente)** | 93.100 WCoin | 93.000 WCoin | -100 | ✅ Débito único |
| **Csrepro (destinatário)** | 0 WCoin | 0 WCoin | 0 | ✅ Sem crédito no Claim |

**WCoinTransaction**: `df8ba101-0000-73e0-4e80-b2501dd012be` (100 WCoin debitado, descrição: "CashShop Gift: Package 35, Price 95 (Wizard Ring) to Csrepro")

#### Item Entregue

| Campo | Valor |
|-------|-------|
| **Item GUID** | `df8ba101-0000-7188-9100-769a31f9798c` |
| **DefinitionId** | `00000080-000d-0014-0000-000000000000` (Wizard's Ring) |
| **Durability** | 30 |
| **Level** | 0 |
| **ActivatedAtUtc** | `2026-10-30 04:15:01.668347-04` (momento do Claim) |
| **ExpiresAtUtc** | `2026-10-31 04:15:01.668347-04` (exatos 86400s = 24h depois) |
| **DurationSeconds** | 86400 (1 dia) |

#### Storage Consumido

- Gift removido do `CashShopStorageItem` após Claim (linha gift removida)
- Apenas linha legada não-gift permanece (`IsGift=false`, `DurationSeconds=NULL`)

#### Preservação de Itens Históricos

Os 3 Wizard Rings originais do Csrepro permanecem intactos:
- `fc39a101-0000-7801-956e-e6b878c2775b`
- `553ea101-0000-76c7-464d-00e4438cb523`
- `dc43a101-0000-7007-c346-16158c5e00fe`

---

### 3.2 Gate 8B — Desempenho do Claim ✅ PASS

| Métrica | Valor | Critério | Status |
|---------|-------|----------|--------|
| **Duração total Claim** | **182 ms** | < 1000 ms | ✅ PASS |
| `fallbackGetById` | **117 ms** | — | ✅ |
| `lockWait` | 0 ms | — | ✅ |
| `playerContextRows` | 2 ms | — | ✅ |
| `fallbackContext` | 1 ms | — | ✅ |
| `inventorySpace` | 0 ms | — | ✅ |
| `SaveChangesAsync` | 40 ms | — | ✅ |

> **Critério de PASS**: Claim completo **182 ms < 1000 ms** ✅

---

## Gate 8B — CustomMove: ✅ PASS

### Implementação Verificada

| Comando | Handler | Status |
|---------|---------|--------|
| `/bar` | Alias → `CustomMoveChatCommandPlugIn` → `/custommove bar` | ✅ Mapeado em `ChatMessageCommandProcessor` |
| `/custommove bar` | `CustomMoveChatCommandPlugIn.ExecuteAsync` | ✅ Implementado |
| `/move` | `MoveChatCommandPlugIn` (nativo) | ✅ **Não afetado** — nativo tem prioridade |

### Teste Presencial Confirmado pelo Proprietário

| Teste | Ação | Resultado |
|-------|------|-----------|
| **B1** | Digitar `/bar` | Move para coordenadas do `bar` (CustomMoveEntry) |
| **B2** | Digitar `/custommove bar` | Mesmo resultado que B1 |
| **B3** | Digitar `/move 10 20` | Move nativo (sem interferência) |
| **B4** | `/custommove inexistente` | Mensagem "Comando não encontrado" nativa |

---

## Gate 8C — Aparência Final: ✅ PASS

### Confirmação do Owner (Visual)

| Cenário | Cliente SkinFix | Resultado |
|---------|-----------------|-----------|
| **C1** | Cshomolo equipa Skeleton Ring | Próprio Hero aparece como Skeleton |
| **C2** | Csrepro entra (Cshomolo já transformado) | Cshomolo visível como Skeleton |
| **C3** | Cshomolo desequipa | Ambos veem Cshomolo humano |
| **C4** | Cshomolo reequipa | Ambos veem Cshomolo Skeleton |
| **C5** | Cshomolo sai/volta viewport | Transformação mantida |
| **C6** | Panda Ring | Modelo Panda correto |
| **C7** | Regressão equipamentos | Visual normal sem anel |

### Correções Aplicadas no MuMain SkinFix

| Fix | Arquivo | Mudança |
|-----|---------|---------|
| **F1** (spawn invisível) | `WSclient.cpp:2651` | `c->Object.AlphaTarget = 1.f` no CreateFlag |
| **F2** (re-equip humano) | `WSclient.cpp:2661-2676` | `SubType = GetTransformSubType(Type)` sempre |
| **F3** (Hero local) | `ZzzCharacter.cpp` | `GetTransformSubTypeFromRings()` em `SetCharacterClass(Hero)` |

**Binário SkinFix:** `C:\Users\isaia\OneDrive\Desktop\Mu Offline - Homologacao Fase 10 - SkinFix\Main.exe`  
SHA256: `DCE427CC6E82CC8218FE3C1F11F3570F3A454350478666873937A05AA5584766`  
Original preservado: `D7AEA4AA21C4F0DBD81C7D94AF9B64C5D1A41FC3487B4EB46EA06A4D6D3AF028`

---

## Gate 8D — Integridade e Regressão: ✅ PASS

### Verificações Realizadas

| Verificação | Status | Detalhes |
|-------------|--------|----------|
| **RC2 operacional** | ✅ | `docker ps`: openmu Up, RestartCount=0, saudável |
| **Banco correto** | ✅ | `openmu_cashshop_validation` (PostgreSQL nativo Windows, 59 migrations) |
| **Migration de expiração** | ✅ | `20261022071625_AddCashShopItemExpiry` aplicada (59ª migration) |
| **Colunas de expiração** | ✅ | `DurationSeconds`, `ActivatedAtUtc`, `ExpiresAtUtc` presentes |
| **WCoin/Storage limpos** | ✅ | Wallet: Cshomolo=93.100, Csrepro=0; Storage: 1 linha legada |
| **Exceções aparência** | ✅ | 0 `Error`/`Exception` em logs recentes |
| **Regressão Cash Shop** | ✅ | Catalog 22 cat / 212 pkg / 2720 prod |
| **Regressão Inventário** | ✅ | 3 Wizard Rings originais preservados + 1 novo |
| **Reinicializações inesperadas** | ✅ | Container up, RestartCount=0 |

---

## Gate 8E — Preparação de Release: ✅ PRONTO

### 1. Git Status — Worktree OpenMU

```bash
# Branch: checkpoint/cashshop-phase10-isolated
# HEAD: 9b0b68292 "docs(cashshop): close phase 10 gift storage"
# 48 arquivos modificados, +2174/-362 linhas
# 20 arquivos novos (migrations, testes, docs, serviços)
```

**Alterações por domínio (Fase 10):**

| Domínio | Arquivos-chave |
|---------|----------------|
| **Cash Shop Core** | `CashShopStorageItem.cs`, `CashShopStorageService.cs`, `CashShopGiftService.cs`, `CashShopStorageClaimService.cs`, `CashShopStorageListAction.cs` |
| **Gift/Claim** | `CashShopGiftService.cs` (+DurationSeconds), `CashShopStorageClaimService.cs` (tracing + expiração atômica) |
| **CustomMove** | `CustomMoveChatCommandPlugIn.cs`, `CustomMoveService.cs`, `ChatMessageCommandProcessor.cs` |
| **SkinFix (MuMain)** | `WSclient.cpp` (F1+F2), `ZzzCharacter.cpp` (F3) — **fora do repo OpenMU** |
| **Expiração** | `ItemExpiryPolicy.cs`, `ExpiredTemporaryItemService.cs`, `20261022071625_AddCashShopItemExpiry` migration |
| **Testes** | `CashShopStorageListActionTests.cs`, `CashShopDurationContractTests.cs`, `CashShopExpiryCleanupTests.cs`, `CustomMove*Tests.cs` |
| **Docs** | `phase-10-gift-storage-final.md`, `phase-10-gate8-final.md`, `item-expiry-contract.md`, `phase-10-closure.md` |

### 2. Git Status — MuMain (SkinFix)

```bash
# Branch: main (up to date with origin/main)
# Modificados:
#   src/source/Network/Server/WSclient.cpp        (+14)  ← F1 + F2
#   src/source/Engine/Object/ZzzCharacter.cpp     (+46)  ← F3
#   src/source/GameShop/MsgBoxIGSSendGift.cpp     (preexistente)
#   src/source/GameShop/ShopListManager/ShopList.cpp (preexistente)
```

### 3. Artefatos Binários

| Artefato | SHA256 | Localização |
|----------|--------|-------------|
| **Main.exe Original (Homologação)** | `D7AEA4AA21C4F0DBD81C7D94AF9B64C5D1A41FC3487B4EB46EA06A4D6D3AF028` | `.../Homologacao Fase 10/Main.exe` |
| **Main.exe SkinFix** | `DCE427CC6E82CC8218FE3C1F11F3570F3A454350478666873937A05AA5584766` | `.../Homologacao Fase 10 - SkinFix/Main.exe` |
| **OpenMU RC2 Server** | `munique/openmu:cashshop-phase10-rc2` | Docker Hub / Local |

### 4. Checklist de Rollback

| Componente | Procedimento |
|------------|--------------|
| **Server RC2** | `docker compose down && docker compose up -d` (imagem imutável) |
| **Banco openmu_cashshop_validation** | `pg_restore -Fc -d openmu_cashshop_validation backup_pre_fase10_*.dump` |
| **Banco openmu (Docker)** | `pg_restore -Fc -d openmu backup_pre_expiry_*.dump` |
| **Cliente SkinFix** | Apagar pasta `.../Homologacao Fase 10 - SkinFix/` |
| **Cliente Original** | Intacto em `.../Homologacao Fase 10/` |
| **Worktree OpenMU** | `git stash` ou `git restore .` (branch isolado) |
| **MuMain** | `git restore src/source/Network/Server/WSclient.cpp src/source/Engine/Object/ZzzCharacter.cpp` |

### 5. Proposta de Commits Separados

| Repositório | Commit Proposto | Escopo |
|-------------|-----------------|--------|
| **OpenMU** | `feat(cashshop): phase 10 gift storage + expiry` | Gift, Claim, Storage, Expiração, CustomMove, Testes |
| **OpenMU** | `feat(cashshop): custommove command isolation` | `/custommove` desacoplado de `/move` |
| **MuMain** | `fix(transform): skeleton/panda ring visibility (F1/F2/F3)` | AlphaTarget, SubType update, Hero local |

---

## Documentação Atualizada

| Arquivo | Status |
|---------|--------|
| `docs/cashshop/phase-10-gift-storage-final.md` | ✅ Atualizado |
| `docs/cashshop/phase-10-gate8-final.md` | ✅ Este arquivo (corrigido) |
| `docs/cashshop/phase-10-closure.md` | ✅ Criado |
| `docs/cashshop/item-expiry-contract.md` | ✅ Existente |
| `docs/cashshop/phase10-architecture-audit.md` | ✅ Existente |
| `docs/cashshop/phase10-release-validation.md` | ✅ Existente |
| `docs/cashshop/phase10-predeploy-gonogo.md` | ✅ Existente |
| `docs/cashshop/catalog-compatibility-2s.csv` | ✅ Existente |
| `docs/Packets/C2-45-AddTransformedCharactersToScopeExtended_by-server.md` | ✅ Atualizado (SkinFix) |

---

## Pendências Reais (Aceitas)

| ID | Descrição | Status |
|-----|-----------|--------|
| **P1** | Deploy da imagem RC2 em produção com variáveis corretas | **Pendente autorização** |
| **P2** | Deploy do Main.exe SkinFix em produção | **Pendente autorização** |
| **P3** | Commit/Push/Tag | **Não autorizado** — aguarda GO final |

---

## Decisão GO/NO-GO

| Critério | Status |
|----------|--------|
| **Gate 8A (Gift/Claim)** | **GO** — Execução real confirmada, reconciliação completa |
| **Gate 8B (CustomMove)** | **GO** — Testado e aprovado presencialmente |
| **Gate 8C (Aparência)** | **GO** — Owner aprovou visualmente (Skeleton + Panda) |
| **Gate 8D (Integridade)** | **GO** — Read-only OK, 59 migrations, 0 erros |
| **Gate 8E (Release Prep)** | **GO** — Artefatos, docs, rollback prontos |

---

## Próximos Passos (Owner decide)

1. **Autorizar commits separados** (OpenMU + MuMain) → **FASE 10 HOMOLOGADA**
2. **Deploy em produção** com imagem RC2 + Main.exe SkinFix
3. **Aplicar migration de expiração** no banco de produção `openmu` (se necessário)

---

## Assinatura

**Preparado por:** Agente de Homologação Fase 10  
**Data:** 2026-10-30  
**Ambiente:** RC2 Docker (`munique/openmu:cashshop-phase10-rc2`) + SkinFix Client  
**Banco de Homologação:** `openmu_cashshop_validation` (PostgreSQL nativo Windows, 59 migrations)  
**Próxima revisão:** Após autorização de versionamento e deploy.