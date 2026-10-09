# Fase 9 — Cash Shop Storage: Investigação fato-a-fato

> **Atualização (implementação do núcleo):** a implementação autorizada desta investigação foi
> realizada e validada — ver a seção **"Implementação do núcleo (realizada)"** ao final. As
> lacunas CRÍTICA (ciclo de retirada) e ALTA (gift sem Quantity) estão **resolvidas**; as demais
> permanecem como registradas. Suíte atual: Full 640/640, CashShop 119/119 GREEN.

**Data do registro:** 2026-10-08
**Escopo:** investigação do Cash Shop Storage existente antes de qualquer implementação. Rodada de DESCOBRIR / PROVAR / TESTAR / PLANEJAR — `src/` NÃO foi alterado nesta rodada de investigação, D2/MuMain/Main.exe intactos, PostgreSQL somente leitura, sem migration.
**Baseline:** branch `checkpoint/cashshop-property-resolution-20261004`, HEAD `9d6041763` (tag `cashshop-phase8-complete`), CashShop 113/113 e Full suite 627/627 GREEN na entrada.
**Fontes:** código real do repositório (arquivo+linha), MuMain (`C:\MuMain`, somente leitura — structs `WSclient.h`, dispatcher `WSclient.cpp:14441-14501`, GameShop), banco `openmu_cashshop_validation` (SELECT/information_schema apenas), e 4 investigações paralelas consolidadas (protocolo, modelo/serviços, testes, PostgreSQL/Quantity).

## 1. Estado atual

- Compra (D2 03) e gift (D2 04) criam `CashShopStorageItem` atômicamente com o débito (unidade de trabalho única do EF; rollback completo; lock por conta). Fase 8 homologada.
- Listagem (D2 05 → D2 06 → D2 0D) implementada e agora com cobertura comportamental (4 testes novos GREEN).
- **Retirada/consumo/remoção: NÃO EXISTE no servidor** — nenhum handler D2 0A/0B, nenhuma action/service de claim, nenhum `DeleteAsync` sobre a entidade. O cliente envia D2 0B e recebe **silêncio** (subcódigo sem handler → `PacketHandlerPlugInContainer.cs:109-113` retorna sem responder; sem crash, sem timeout/retry no cliente).

## 2. Protocolo D2 Storage (CONFIRMADO por 3 fontes: struct legacy C++, XML/Ref do OpenMU, call sites)

| Direção | Código/Sub | Struct (MuMain, `WSclient.h`) | Tamanho no fio | Campos | Handler servidor | Finalidade |
|---|---|---|---|---|---|---|
| C2S | D2 05 | `PMSG_CASHSHOP_STORAGELIST_REQ` | 10 (byte @9 = lixo do buffer pooled; handler deve ler só até @8) | PageIndex u32 LE @4, StorageType char @8 ('S' compras / 'G' presentes) | `CashShopStorageListRequestHandlerPlugIn` → `CashShopStorageListAction.RequestListAsync` | Pedir página do storage |
| S2C | D2 06 | `PMSG_CASHSHOP_STORAGECOUNT` | 12 | 4 WORDs: TotalItemCount, CurrentItemCount, PageIndex, TotalPage | writer `SendCashShopStorageCountAsync` | Contagem/init da página (limpa a lista no cliente) |
| S2C | D2 0D | `PMSG_CASHSHOP_STORAGELIST` | 33 | StorageIndex @4, ItemSeq @8, StorageGroup @12, ProductSeq @16, PriceSeq @20, CashPoint double @24, ItemType char @32 | writer `SendCashShopStorageItemAsync` | Item da lista de **compras** (aba S) |
| S2C | **D2 0E** | `PMSG_CASHSHOP_GIFTSTORAGELIST` | 244 | idem 0D + `chSendUserName[MAX_USERNAME_SIZE+1=11]` UTF-8 + `chMessage[MAX_GIFT_MESSAGE_SIZE=200]` UTF-8 | **NÃO EXISTE** | Item da lista de **presentes** (aba G) com remetente/mensagem |
| C2S | D2 0A | `PMSG_CASHSHOP_STORAGE_ITEM_THROW_REQ` | 13 legacy / **234 no fio** (Length do XML; campos reais só até @12) | StorageSeq @4, StorageItemSeq @8, StorageItemType @12 | **NÃO EXISTE** | "Jogar fora" (delete) |
| S2C | D2 0A ans | `PMSG_CASHSHOP_STORAGE_ITEM_THROW_ANS` | 5 | byResult @4 | — | **Definida e nunca usada**: o dispatcher do cliente **não tem case 0x0A** |
| C2S | **D2 0B** | `PMSG_CASHSHOP_STORAGE_ITEM_USE_REQ` | 15 | StorageSeq @4, StorageItemSeq @8, ItemCode u16 @12, StorageItemType @14 | **NÃO EXISTE** | **RETIRADA/USO** (aplica ou adiciona ao inventário) |
| S2C | **D2 0B ans** | `PMSG_CASHSHOP_STORAGE_ITEM_USE_ANS` | 5 | byResult @4 | **NÃO EXISTE** | Resposta da retirada |

**Nomenclatura enganosa CONFIRMADA**: os campos `BaseItemCode`/`MainItemCode` das structs C# (`CashShopDeleteStorageItemRequest`, `CashShopStorageItemConsumeRequest`) carregam **(StorageSeq, StorageItemSeq)** — não item codes. Call sites do MuMain: `MsgBoxIGSDeleteItemConfirm.cpp:134` (0A, código morto), `MsgBoxIGSUseItemConfirm.cpp:164` / `MsgBoxIGSUseBuffConfirm.cpp:113` (0B). O ItemCode do jogo (group*512+number) só viaja no **D2 0B** (@12, `wItemCode`, derivado do **catálogo local do cliente** — pode divergir do catálogo do servidor; os seqs devem ser a autoridade) e no D2 03 (compra).

**Respostas objetivas (D2 0D):**

```text
ItemCode viaja no D2 0D?  NÃO — resolvido no catálogo local (GetProductInfoFromPriceSeq,
                          InGameShopSystem.cpp:588-626; ITEMCODE = InGamePackageID, :723-728)
ProductSeq viaja?         SIM (@16-19)
PriceSeq viaja?           SIM (@20-23)
StorageIndex identifica o quê?  O registro (linha) do item no storage DA CONTA — não é o
                          ItemCode; é o 1º identificador reenviado nos D2 0A/0B (como
                          BaseItemCode); o par (StorageIndex, ItemSeq) identifica a entrada
```

**Códigos de resultado do D2 0B (cliente `ReceiveIGS_UseStorageItem`, WSclient.cpp:12716-12804):**

```text
0    = sucesso → "ItemUsed" + UpdateStorageItemList() (reenvia D2 05)
-1   (255) = erro
-2   (254) = erro de banco
1    = use storage não existe
2    = só PC Café
3/4  = plano ativo no período
21   = SEM ESPAÇO NO INVENTÁRIO   ← código específico para inventário cheio
22   = não pode usar
24   = conflito com item em uso
Sem resposta = nada acontece (sem timeout/retry)
```

## 3. Fluxo atual

```text
Purchase → Storage (CONFIRMADO, com linhas):
  Handler D2 03 → CashShopBuyAction → CashShopBuyService.ProcessPurchaseAsync (:93-255)
  → lock por conta AsyncLock (:107) → resolver (:112) → quantidade (:134)
  → ResolveDeliveredDurabilities (:343-398; Quantity → Item.Durability)
  → reserva de slot no MESMO contexto (ReserveNextStorageIndexAsync, :164-166)
  → cria Item (:175) → preenche a linha (:179-187) → débito WCoin (:190)
  → SaveChangesAsync único (:206; = transação EF) → rollback completo (DiscardStagedPurchaseAsync :413-469)
    + UnsafeContextRetirement em falha de rollback

Gift → Storage (CONFIRMADO):
  CashShopGiftService.ProcessGiftAsync (:91-260): mesma mecânica; resolve receptor ANTES de
  stagear (:147-154); reserva na conta do receptor com forGift=true (:171-177; IsGift=true
  na reserva, CashShopStorageService.cs:168); GiftText lido e DESCARTADO (remarks :47-61);
  ⚠️ NÃO usa selection.Quantity (Durability = 1 em stackables, :186-188)

Storage → Inventory (CONFIRMADO: NÃO EXISTE):
  Sem handler D2 0A/0B, sem action/service de claim, sem remoção da linha. A linha só é
  removida por CASCADE de banco (apagar conta ou Item). Quem recupera? NINGUÉM ainda.
```

## 4. Modelo — campos reais de `CashShopStorageItem` (`src/DataModel/Entities/CashShopStorageItem.cs`)

| Campo | Tipo / obrigatório | Origem | Uso listagem | Uso retirada | Classificação |
|---|---|---|---|---|---|
| Id | Guid (PK, gerado no banco) | reserva | não viaja | — | existente e usado (persistência) |
| AccountId | Guid, obrigatório | reserva (:166) | filtro por conta (:198) | — | existente e usado |
| StorageIndex | int, obrigatório | alocado na reserva (:167; por aba) | viaja no D2 0D (:114); ordenação | é o identificador que o cliente devolve (D2 0B) | existente e usado |
| ItemSeq | int, obrigatório | = StorageIndex (Buy :180; Gift :190) | viaja no D2 0D (:115) | 2º identificador (D2 0B) | existente e usado |
| StorageGroup | int, obrigatório | selection.StorageGroup | viaja no D2 0D (:116) | — | existente e usado |
| ProductSeq | int, obrigatório | selection.ProductSequence | viaja no D2 0D (:117) | — | existente e usado |
| PriceSeq | int, obrigatório | selection.SelectedPriceSequence | viaja no D2 0D (:118) | — | existente e usado |
| CashPoint | double, obrigatório | preço | viaja no D2 0D (:119) | — | existente e usado |
| ItemType | string(1), "P" | sempre Product | 1º char no D2 0D (:111) | — | existente e usado |
| Item | Item? (`[MemberOfAggregate]`) | criado na compra/gift | NÃO viaja | **o item a entregar na retirada** | existente, gravado; nada o lê ainda |
| IsGift | bool, default false | reserva (forGift) | filtro da aba 'G' | — | existente e usado |
| PurchasedAt | DateTime, default UtcNow | inicializador | NÃO viaja | — | existente mas não usado |

**Confirmação expressa (entidade E banco, information_schema):**

```text
Quantity:        NÃO EXISTE (coluna/propriedade; a quantidade vive em Item.Durability)
DurationSeconds: NÃO EXISTE
ExpiresAt:       NÃO EXISTE
GiftSender:      NÃO EXISTE (o cliente recebe o remetente pelo D2 0E)
GiftMessage:     NÃO EXISTE (o GiftText do D2 04 é descartado)
ClaimedAt:       NÃO EXISTE
```

## 5. Segurança (ownership)

```text
Ownership:                   a linha pertence à CONTA (AccountId); o cliente envia só
                             (StorageIndex, ItemSeq[, ItemCode]) — a conta é sempre a do
                             jogador autenticado (player.Account), nunca informada pelo cliente
Account scoped:              SIM na listagem (GetPageAsync/GetGiftCountAsync por account.Id,
                             CashShopStorageListAction.cs:82-97) e na reserva;
                             a futura retirada DEVE ler a linha por (AccountId do jogador,
                             StorageIndex, ItemSeq) — nunca por índice global
Risco de StorageIndex arbitrário:  HOJE inoperante (não há handler 0A/0B — um índice forjado
                             é silenciosamente ignorado). Na implementação: um índice que não
                             pertence à conta do jogador deve ser recusado (não encontrado) —
                             nunca entregar linha de outra conta. SEM COBERTURA de teste ainda
                             (prioridade para os testes da implementação)
```

## 6. Atomicidade da retirada

```text
Resultado: INCONCLUSIVA — não existe retirada implementada.
Evidência: nenhum handler D2 0A/0B, nenhum claim; nenhuma ordem criação/remoção para auditar.
Propriedade obrigatória (OU mantém no Storage OU recebe o Item; NUNCA os dois; NUNCA nenhum)
deve ser garantida na implementação com o MESMO padrão da compra: remove a linha + cria/move
o Item no MESMO contexto e um único SaveChangesAsync (transação EF), com rollback completo
(DiscardStaged* + UnsafeContextRetirement).
```

## 7. Concorrência (duplo claim)

```text
Double claim protegido: INCONCLUSIVO — não existe claim; nenhum teste.
Padrão disponível: o lock por conta (AsyncLock, padrão da compra/gift) serializa as
requisições do mesmo jogador; o par (StorageIndex, ItemSeq) + estado da linha (removida no
mesmo commit) + teste RED planejado garantem 1 linha = no máximo 1 item entregue.
```

## 8. Quantity (Fase 8 → Fase 9)

```text
Compra direta:  Item.Durability = quantity (stack count) — CONFIRMADO (Buy :175-177, :343-398)
Storage:        a row NÃO tem coluna de quantidade; a informação persistida vive SOMENTE em
                Item.Durability (linha data."Item" apontada por ItemId; FK 1:1 UNIQUE)
Gift:           ⚠️ NÃO preserva — Durability = 1 em stackables (Gift :186-188 não usa
                selection.Quantity). Comprovado com teste RED:
                ProcessGiftAsync_Quantity20_PreservesQuantityInStorageItem
                (Expected: 20, But was: 1.0d)
Retirada:       não existe; quando existir, o Item já referenciado pela linha carrega a
                durability real — a Quantity de uma compra sobrevive; a de um gift já nasce errada
Preserva Quantity: COMPRA SIM / GIFT NÃO (lacuna real comprovada)
```

A listagem (D2 0D) não transporta durability — o cliente exibe a quantidade do **catálogo local** (ProductSeq/PriceSeq); consistente enquanto o item não é consumido.

## 9. Duration

```text
Resolver:  resolvida (AggregateDuration → CashShopSelectionResult.DurationSeconds) e usada
           apenas em memória durante a request
Storage:   NÃO persistida (nenhum campo na linha/entidade; nenhuma coluna no banco)
Retirada:  não existe
Persistida: NÃO — PERDIDA após a entrega
```

Expiração continua FORA da Fase 9 (sem ExpiresAt/timer/job/migration/remoção temporal).

## 10. Gift + Storage (claim do destinatário)

```text
Estado: a Fase 8 homologou Gift → CashShopStorageItem do destinatário (aba 'G').
Claim do presente: NÃO EXISTE (mesmo fluxo ausente da retirada).
Na implementação, o claim do presente deve preservar: ProductSeq/PriceSeq (da linha),
Item correto (referenciado), Quantity (do Item.Durability), e NÃO sofrer segundo débito
(a retirada não consulta WCoin — débito zero).
D2 0E: o destinatário hoje NÃO vê remetente/mensagem (o servidor envia a aba 'G' pelo
D2 0D genérico; o cliente recebe szID/szMessage apenas pelo D2 0E). Lacuna de UX.
```

## 11. Item 6702 (durante claim)

```text
Comportamento durante claim: NÃO EXISTE claim; na implementação, a retirada deve falhar de
forma segura quando ItemDefinition não resolve
Storage preservado: a implementação NÃO PODE remover a linha antes de validar o ItemDefinition
Item não criado: a implementação não deve criar Item sem Definition
Resultado seguro hoje: sem handler, nenhuma requisição é processada (nada acontece)
Regra para a implementação: validar ItemDefinition ANTES de remover a linha (remover antes
de descobrir seria BUG CRÍTICO — perda de item)
```

## 12. PostgreSQL (`openmu_cashshop_validation`, somente leitura)

```text
Tabelas:  data."CashShopStorageItem" (12 colunas), data."Item", data."ItemStorage",
          data."WCoinWallet" (shared-PK: Id = Account.Id), data."Account"
Constraints: PK_CashShopStorageItem(Id); sem UNIQUE/CHECK de tabela
FKs: AccountId → Account.Id CASCADE; ItemId → Item.Id CASCADE (1:1 UNIQUE);
     Item.ItemStorageId → ItemStorage CASCADE; Item.DefinitionId → ItemDefinition (NO ACTION)
Índices relevantes: IX_CashShopStorageItem_ItemId UNIQUE; IX_..._AccountId;
     IX_..._AccountId_IsGift; IX_..._AccountId_IsGift_StorageIndex UNIQUE (por aba)
Contagens: storage = 0 linhas; WCoinTransaction = 5 (gifts da homologação Fase 8;
     todas Type=1 Purchase com descrição "CashShop Gift: ...")
⚠️ Divergência documental: o comentário de CashShopBuyService.cs:58-59 diz
     "unique index (AccountId, StorageIndex)", mas o índice real é
     (AccountId, IsGift, StorageIndex) — unicidade POR ABA. A lógica de reserva está
     alinhada com o índice real; o comentário está desatualizado.
```

## 13. Testes

**Existentes (inventário):**

| Arquivo | Testes | Comportamento | Estado |
|---|---|---|---|
| `CashShopStorageServiceTests.cs` | 13 | alocação/reserva de StorageIndex (primeiro livre, buracos, escopo por conta, sinal de cheio, reserva no contexto do chamador, 2 reservas não reusam slot, abas S/G com numeração independente), paginação/contagem | GREEN |
| `CashShopBuyServiceTests.cs` | 52 execuções | compra completa + agregação + rejeições + concorrência + atomicidade (rollback/cancelamento/retirement/constraint) | GREEN |
| `CashShopGiftServiceTests.cs` | 9+1 execuções | GiftFlag 184, entrega ao destinatário, rollback/cancelamento | GREEN (+1 RED novo) |
| `CashShopCatalog*Tests.cs` | 25 | catálogo/parser/seed/paridade | GREEN |
| `CashShopStorageItemMigrationTests.cs` | 3 | migration/modelo (unicidade por aba, coluna IsGift) | GREEN |
| `CashShopPacketTests.cs` (S2C) / `ClientToServerPacketTests.cs` | 8 + 8 | layouts de pacote / tamanhos C2S | GREEN |

**Novos desta rodada:**

| Teste | Estado | Comportamento |
|---|---|---|
| `CashShopStorageListActionTests.RequestListAsync_PurchaseTab_SendsCountAndAccountScopedItems` | **GREEN** | D2 06 + D2 0D por conta, com os campos que o cliente resolve no catálogo local |
| `CashShopStorageListActionTests.RequestListAsync_GiftTab_ListsOnlyGiftRows` | **GREEN** | aba 'G' lista só presentes, numeração independente |
| `CashShopStorageListActionTests.RequestListAsync_UnknownStorageType_AnswersWithEmptyList` | **GREEN** | aba forjada recusada com lista vazia |
| `CashShopStorageListActionTests.RequestListAsync_PageBeyondRange_AnswersCountsOnly` | **GREEN** | página fora do range responde contagens apenas |
| `CashShopGiftServiceTests.ProcessGiftAsync_Quantity20_PreservesQuantityInStorageItem` | **RED (intencional)** | demonstra a lacuna: o gift entrega Durability=1 em vez de 20 |

**Estado da suíte após os testes novos:** Full suite **632 total / 631 passed / 1 failed (RED intencional)**; CashShop **118 / 117 / 1**.

**Sem cobertura (todas SEM COBERTURA — confirmado por inventário):** retirada/claim/consume/delete (nenhum teste comportamental), duplo claim, inventário cheio na retirada, IDOR na retirada, ItemDefinition inexistente em operação de storage, atomicidade "cria item no inventário + remove storage no mesmo commit". Nascem com a implementação (a interface de claim ainda não existe — testes contra API inexistente não compilam sem `src/`).

## 14. Lacunas classificadas

| Lacuna | Classificação |
|---|---|
| Ciclo de retirada (claim → inventário) inexistente: sem handler D2 0B, sem serviço, sem remoção da linha — o cliente clica "Use" e nada acontece | **CRÍTICA** (é o objetivo da Fase 9) |
| Gift NÃO preserva Quantity (Durability=1 em stackables) — compra de Quantity=20 via gift entrega 1 | **ALTA** (comprovada com teste RED; mesma natureza do gap corrigido na compra) |
| D2 0E inexistente: destinatário não vê remetente/mensagem do presente | **MÉDIA** (UX; requer campos GiftSender/GiftMessage + migration — passo próprio) |
| Comentário desatualizado do índice (BuyService.cs:58-59: diz global, real é por aba) | **BAIXA** (documentação de código) |
| Nomenclatura enganosa nas structs C# (BaseItemCode/MainItemCode = StorageSeq/StorageItemSeq) | **BAIXA** (não alterar: pacotes são gerados/ClientLibrary consome o layout de fio; documentar apenas) |
| D2 0A: código morto no cliente (dialog nunca instanciado; resposta sem case no dispatcher) | **BAIXA** (delete/throw é dispensável; implementar depois ou nunca, com baixa prioridade) |
| DurationSeconds perdida após a entrega | **BAIXA** (expiração é projeto separado) |

## 15. Proposta de implementação (ordem mínima — NÃO implementada ainda)

1. **Serviço de retirada** (`ICashShopStorageService.ClaimAsync` ou novo `CashShopStorageClaimService`):
   - resolve a linha por (AccountId do jogador autenticado, StorageIndex, ItemSeq) — account-scoped, sem IDOR;
   - valida ItemType 'P' e que a linha existe (inexistente → resultado de erro, sem efeito);
   - valida o Item/ItemDefinition **ANTES** de remover a linha (indisponível → linha preservada);
   - valida espaço no inventário do personagem (cheio → código **21** do USE_ANS, linha preservada);
   - move/entrega o Item referenciado (preserva Durability = Quantity) ou cria Item do catálogo; remove a linha no **MESMO contexto**;
   - um único `SaveChangesAsync` (transação EF), rollback completo (padrão `DiscardStaged*` + `UnsafeContextRetirement`), lock por conta;
   - **débito WCoin ZERO** (gift/compra já cobradas na entrada).
2. **Handler D2 0B (Use)**: parse (StorageSeq @4, StorageItemSeq @8, ItemCode @12, ProductType @14; tolerar tamanho), resposta **D2 0B ans** (5 bytes, byResult em two's complement: 0/-1/-2/21/22/24…). O cliente trata em `ReceiveIGS_UseStorageItem` e reenvia D2 05 no sucesso.
3. **Testes** (a partir da interface nova): retirada válida (Item no inventário + linha removida no mesmo commit), inventário cheio (código 21, linha preservada), StorageIndex inexistente/de outra conta (recusa, nada muda), duplo claim (no máximo 1 item), falha de commit (rollback completo), ItemDefinition inexistente (linha preservada, sem item, sem exceção), Quantity preservada (Durability do Item), Gift → claim (sem segundo débito).
4. **Correção do gift Quantity** (o RED já criado): usar `selection.Quantity` no gift, igual à compra (com as mesmas regras de capacidade do `ResolveDeliveredDurabilities`).
5. **D2 0E** (opcional, passo próprio com migration: GiftSender/GiftMessage na entidade) para o destinatário ver remetente/mensagem.
6. **Documentação de código**: corrigir o comentário desatualizado do índice (BuyService.cs:58-59); documentar a nomenclatura BaseItemCode=StorageSeq.
7. **D2 0A (Throw)**: opcional/adiável — código morto no cliente; só implementar se o fluxo de descartar for desejado.

## 16. Integridade

```text
src/ inalterado ✓ (só testes novos + este documento)
D2 inalterado ✓   MuMain inalterado ✓   Main.exe inalterado ✓
PostgreSQL schema inalterado ✓ (somente leitura)   sem migration ✓   sem Docker ✓
sem push ✓   Fase 8 intacta ✓ (627 GREEN preservados + 4 novos + 1 RED intencional)
```

---

## Implementação do núcleo (realizada)

**Rodada autorizada após a investigação.** Estado final: **Full suite 640/640 GREEN, CashShop 119/119 GREEN, 0 failed, 0 skipped.**

### 1. Correção de Quantity no Gift (lacuna ALTA)

- `CashShopBuyService.ResolveDeliveredDurabilities` virou helper **estático compartilhado** (`internal static`, com logger explícito) e o `CashShopGiftService` passou a usar a mesma regra validada da compra (sem duplicação de regra):
  - stackable: `Durability = Quantity` (≥ 1 e ≤ `ItemDefinition.Durability`; acima → recusa);
  - não-stackable com `Quantity > 1` → recusa;
  - validação **antes** de qualquer estado ser montado (sem tocar a carteira ou o slot do destinatário).
- O teste RED (`ProcessGiftAsync_Quantity20_PreservesQuantityInStorageItem`) ficou **GREEN** sem ser alterado — o comportamento foi corrigido, não o teste.

### 2. ItemSeq global único por conta (identificação sem ambiguidade de aba)

A investigação provou que o D2 0B **não carrega a aba** e que o par `(StorageIndex, ItemSeq)` colidia entre as abas ('S' e 'G' podem ter o mesmo índice — entrega do item errado). Correção:

- `CashShopStorageService.ReserveNextStorageIndexAsync` aloca agora o `ItemSeq` como **sequência única por conta, cobrindo as duas abas** (`max(ItemSeq) + 1` no MESMO contexto/transação; 0 quando a conta não tem linhas);
- compra e gift não sobrescrevem mais o `ItemSeq` (a reserva o define);
- o D2 0D continua enviando ambos os campos (o cliente os ecoa opacamente); a listagem continua ordenada por `StorageIndex` (posição na aba);
- os 13 testes de alocação existentes continuam GREEN (só assertam `StorageIndex`).

### 3. Serviço de retirada (`CashShopStorageClaimService` + `ICashShopStorageClaimService`)

- **Autoridade server-side**: a linha persistida resolvida por `(AccountId == player.Account.Id, ItemSeq == request.StorageItemSeq, StorageIndex == request.StorageSeq, ItemType 'P')` — a conta é sempre a do jogador autenticado; o ItemCode do cliente **nunca é autoridade** (só log para diagnóstico);
- **validações ANTES de consumir** (nunca remove primeiro): linha existe → pertence à conta → Item existe → ItemDefinition existe → tipo suportado → espaço no inventário (`CheckInvSpace`);
- **entrega**: o item referenciado pela linha (com sua `Durability` = Quantity preservada) vai ao inventário via `AddItemAsync` (infraestrutura existente, mesmo padrão do `BuyNpcItemAction`); **débito WCoin ZERO** (a compra/gift já cobraram na entrada);
- **atomicidade**: o item move e a remoção da linha no MESMO contexto + um único `SaveChangesAsync` (transação EF) — OU o item permanece no storage OU é entregue ao inventário, nunca ambos, nunca nenhum;
- **segurança de banco verificada**: o cascade é **Item → row** (deletar o item remove a linha; o reverso não acontece) — comprovado empiricamente no banco de homologação com dados descartáveis (inseridos e limpos), então o item referenciado **sobrevive** à remoção da linha;
- **rollback**: em qualquer falha (save `false`, exceção, cancelamento), o item sai do inventário em memória, o `ItemSlot` anterior é restaurado e a linha + o item são desanexados do contexto (a linha permanece no banco); rollback incompleto → `UnsafeContextRetirement` retira a sessão (padrão da compra/gift);
- **concorrência**: AsyncLock por conta; duplo claim → o segundo não encontra a linha (consumida no primeiro commit) → 1 linha = no máximo 1 item entregue.

### 4. Handler D2 0B + resposta

- `CashShopStorageItemConsumeRequestHandlerPlugIn` (D2 0B, `[PlugIn]` auto-descoberto, `BelongsToGroup` 0xD2): parse tolerante (rejeita < 13 bytes; `BaseItemCode/MainItemCode > int.MaxValue` → `StorageNotFound`); registra o item code do cliente apenas para diagnóstico;
- resposta **D2 0B** (5 bytes) pela view: `ICashShopViewPlugIn.ShowStorageItemUseResultAsync` + `CashShopViewPlugIn` → `SendCashShopStorageItemUseResultAsync` — códigos em complemento de dois (0/-1/-2/21/22), ditados por `ReceiveIGS_UseStorageItem`;
- o cliente renderiza o resultado sozinho (message boxes) e reenvia D2 05 no sucesso — nenhuma outra resposta é enviada;
- DI registrado em `src/Startup/Program.cs` (interface + concreto) e no helper de wiring dos testes.

### 5. Testes (novos — `CashShopStorageClaimTests.cs`, 8 GREEN)

| Teste | Cenário |
|---|---|
| `ClaimAsync_ValidRow_MovesItemToInventoryAndConsumesRow` | retirada válida: item no inventário (Quantity preservada) + linha consumida no mesmo commit |
| `ClaimAsync_FullInventory_RowStaysAndNothingIsDelivered` | inventário cheio: código 21, linha permanece, nada entregue |
| `ClaimAsync_UnknownIndex_RefusedWithoutEffect` | índice inexistente: recusa sem efeito |
| `ClaimAsync_IndexOfAnotherAccount_IsRefusedWithoutSideEffects` | IDOR: 'não encontrado', registro da outra conta intacto |
| `ClaimAsync_Twice_OnlyOneClaimSucceeds` | duplo claim: 1 linha = no máximo 1 item |
| `ClaimAsync_ItemWithoutDefinition_RowStaysAndNoItemIsCreated` | forma 6702: linha preservada, sem item, sem exceção |
| `ClaimAsync_RowWithoutItem_RowStays` | linha sem item entregue: linha permanece |
| `ClaimAsync_GiftRow_PreservesQuantityAndDoesNotDebit` | gift → claim: Quantity preservada, segundo débito ZERO |

### 6. Integridade da implementação

```text
D2 inalterado nos pacotes existentes ✓ (só a resposta 0B nova foi adicionada)
MuMain inalterado ✓   Main.exe inalterado ✓   IBS inalterado ✓
PostgreSQL sem migration nova ✓   Docker inalterado ✓   sem push ✓
sem expiração ✓   sem D2 0E/GiftSender/GiftMessage ✓   sem D2 0A Throw ✓   Item 6702 sem correção ✓
```

### 7. Lacunas restantes (inalteradas)

- D2 0E (remetente/mensagem do presente) — MÉDIA, passo próprio com migration;
- comentário desatualizado do índice (BuyService.cs:58-59) — BAIXA;
- nomenclatura BaseItemCode/MainItemCode nas structs C# — BAIXA (documentada; não alterar o wire format);
- D2 0A Throw — BAIXA (código morto no cliente);
- DurationSeconds perdida — BAIXA (expiração = projeto separado);
- Item 6702 — PENDÊNCIA NÃO BLOQUEANTE (compatibilidade IBS × Season 6).
