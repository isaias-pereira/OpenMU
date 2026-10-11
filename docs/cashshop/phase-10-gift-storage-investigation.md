# Fase 10 — Cash Shop Gift Storage: Investigação (remetente/mensagem + D2 0E)

## Rodada 2B — Implementação e validação automatizada

**Estado: código implementado e validado em teste automatizado. Migration gerada, porém
NÃO aplicada. Nenhum commit/tag/push. Homologação real no PostgreSQL fica para a próxima rodada.**

### Arquivos alterados

| Arquivo | Mudança |
|---|---|
| `src\DataModel\Entities\CashShopStorageItem.cs` | Novas propriedades `string? GiftSender` (limite 10 chars) e `string? GiftMessage` (limite 200 chars), nullable, sem default |
| `src\Persistence\EntityFramework\Extensions\ModelBuilder\CashShopStorageModelBuilderExtensions.cs` | `HasMaxLength(10)`/`HasMaxLength(200)` para os metadados; nenhum índice/FK alterado |
| `src\Persistence\EntityFramework\Migrations\20261011053051_AddCashShopStorageGiftMetadata.cs` (+ `.Designer.cs`) | **Migration aditiva gerada** (`dotnet ef migrations add AddCashShopStorageGiftMetadata --context EntityDataContext`): 2× `AddColumn` nullable no schema `data`; `Down` = 2× `DropColumn`. **NÃO executada** (`database update` não foi rodado) |
| `src\Persistence\EntityFramework\Migrations\EntityDataContextModelSnapshot.cs` | Snapshot atualizado pelo EF (apenas as 2 propriedades novas; reordenação alfabética sem efeito semântico) |
| `src\GameLogic\PlayerActions\CashShop\CashShopGiftService.cs` | Persistência dos metadados antes do único `SaveChangesAsync`; helper `NormalizeGiftMessage` (corta no 1º NUL, vazio/espaços → null, cap 200 chars); remetente = `player.SelectedCharacter!.Name`; doc de classe atualizado (removido "Gift text is NOT processed") |
| `src\Network\Packets\ServerToClient\CashShopConnectionExtensions.cs` | Novo serializer `SendCashShopGiftStorageItemAsync` (244 bytes) + helper `WriteFixedUtf8Field`; tabela de docs da classe com D2 0E |
| `src\GameLogic\Views\CashShop\ICashShopViewPlugIn.cs` | Novo método `ShowGiftStorageItemAsync` |
| `src\GameServer\RemoteView\CashShop\CashShopViewPlugIn.cs` | Implementação do `ShowGiftStorageItemAsync` → serializer D2 0E |
| `src\GameLogic\PlayerActions\CashShop\CashShopStorageListAction.cs` | Aba 'G' → `ShowGiftStorageItemAsync` (D2 0E); aba 'S' → `ShowStorageItemAsync` (D2 0D), inalterado; contagem/paginação/filtro intocados |
| `tests\MUnique.OpenMU.Tests\CashShop\CashShopGiftServiceTests.cs` | +7 testes (remetente da sessão, mensagem, anti-spoofing, mensagem vazia ×2, legado NULL, falha de save sem resíduo) |
| `tests\MUnique.OpenMU.Tests\CashShop\CashShopStorageListActionTests.cs` | +6 testes (gift tab→0E com metadados, purchase tab nunca 0E, paginação 0E, account-scoped com Verify, legado NULL na listagem) + test double `RecordingCashShopViewPlugIn.GiftStorageItems` |
| `tests\MUnique.OpenMU.Tests\CashShop\CashShopStorageClaimTests.cs` | +1 teste (claim de linha com metadados: sem segundo débito, quantity preservada) |
| `tests\MUnique.OpenMU.Network.Packets.Tests\CashShopPacketTests.cs` | +10 métodos/14 execuções: layout 244B, header, offsets, NUL-terminator, limites 10/199 bytes, fronteira UTF-8 ('á' e emoji), NUL embutido, acentos pt-BR, metadados NULL |
| `tests\MUnique.OpenMU.Persistence.Initialization.Tests\CashShopStorageItemMigrationTests.cs` | +2 testes (descoberta da nova migration após a da GiftFlag; colunas nullable com MaxLength 10/200) |
| `tests\MUnique.OpenMU.Tests\CashShop\CashShopStorageListActionTests.cs` — `RequestListAsync_GiftTab_ListsOnlyGiftRows` (pré-existente da Fase 9) | **Atualizado para o novo contrato aprovado**: a aba 'G' entrega as linhas pelo D2 0E (`GiftStorageItems`), não mais pelo D2 0D. A intenção do teste (só linhas de presente, numeração independente) foi preservada |

### RED → GREEN (comportamental, não de compilação)

- **RED observado** (antes da implementação do comportamento): 7 no projeto
  `MUnique.OpenMU.Tests` (3 de persistência Gift + 4 de listagem Gift-tab) e 14 no
  `Network.Packets.Tests` (todos com `NotImplementedException` do stub) — 21 falhas
  comportamentais no total. 6 testes nasceram GREEN por design (guards): mensagem vazia ×2,
  linha legada persistível, purchase tab nunca 0E, falha de save sem resíduo, claim com metadados.
- **GREEN final**: CashShop filter `MUnique.OpenMU.Tests.CashShop` = **139/139**;
  `Network.Packets.Tests` completo = **485/485**;
  `Persistence.Initialization.Tests` completo = **23 passed / 2 ignored (preexistentes) / 0 failed**;
  suíte completa `MUnique.OpenMU.Tests` = executada no fim da rodada (ver relatório).

### Contrato D2 0E implementado

- 244 bytes: `C1` `244` `0xD2` `0x0E`; ints LE @4/8/12/16/20; double LE @24; `chItemType` @32;
  `chSendUserName` @33 (11 bytes); `chMessage` @44 (200 bytes).
- **Sender autenticado**: `CashShopGiftService` grava `player.SelectedCharacter!.Name` — o D2 04
  não tem campo de remetente; nenhum conteúdo do pacote pode falsificá-lo.
- **Sanitização UTF-8** (`WriteFixedUtf8Field`): campo inteiro zerado primeiro (nunca byte não
  inicializado); máximo `fieldSize-1` bytes úteis (10 / 199) com NUL garantido dentro do campo
  (o `wcscpy` do cliente precisa dele); truncamento por fronteira de caractere (caminha de volta
  sobre continuation bytes); conteúdo termina no primeiro NUL embutido; acentos/emoji/controles
  passam sem transformação.
- **NULL legado**: metadados nulos → campos 11/200 bytes zerados; nenhum remetente inventado;
  claim e listagem continuam funcionando.
- **Atomicidade**: metadados atribuídos à linha reservada antes do único `SaveChangesAsync`;
  o rollback existente (`DiscardStagedGiftAsync`) descarta a linha com os metadados junto.

### Correção documental (factual mínima)

**D2 0E = servidor → cliente.** O registro da Fase 9 em `phase-09-storage-final.md` que lista
"D2 0E | cliente → servidor" está incorreto: no MuMain, `0x0E` é tratado no dispatcher de
recebimento do grupo `0xD2` (WSclient.cpp:14480-14482, `ReceiveIGS_StorageGiftItemList`), e o
cliente nunca envia subcode 0x0E. Os commits e a tag da Fase 9 não foram reescritos.

### Pendências para a rodada de homologação

1. Aplicar `20261011053051_AddCashShopStorageGiftMetadata` **exclusivamente** em
   `openmu_cashshop_validation` (`dotnet ef database update` ou script SQL revisado).
2. Homologar Gift com remetente/mensagem de ponta a ponta (D2 04 → storage → D2 0E → claim).
3. Verificar bytes reais do D2 0E no fio (244 bytes, NUL-terminator, padding).
4. Confirmar Gift → Claim com mesmo ItemId e delta WCoin = 0.
5. Suíte final completa + documentação final + commits + tag `cashshop-phase10-complete`.

---

## Status (investigação original)

**FASE 10 — INVESTIGAÇÃO CONCLUÍDA. IMPLEMENTAÇÃO — NÃO INICIADA.** *(ver nota da Rodada 2B acima)*

Esta é uma rodada **somente de investigação e planejamento**. Nenhum arquivo de
`src/`, `tests/`, `tools/` ou migration foi alterado; nenhum comando de escrita
foi executado contra o PostgreSQL; MuMain e Main.exe não foram tocados. A única
alteração do working tree é este documento.

Classificação das afirmações conforme a convenção do projeto (AGENTS.md §8):
**CONFIRMADO** = evidência direta em arquivo/linha citado; **PROVÁVEL** = inferência
explícita a partir de evidência; **DESCONHECIDO** = sem evidência nesta rodada.

## Baseline real (verificado nesta rodada)

| Item | Valor | Evidência |
|---|---|---|
| Branch | `checkpoint/cashshop-property-resolution-20261004` | `git branch --show-current` |
| HEAD | `9bb6b8e3f53bc64492d2ae1c35dfd3bcf96e4444` | `git rev-parse HEAD` |
| Tag Fase 9 | `cashshop-phase9-complete` → mesmo commit do HEAD | `git rev-list -n 1 cashshop-phase9-complete` |
| Suíte completa | **640/640 GREEN** (`Aprovado: 640, Falha: 0, Ignorado: 0` — 3m35s) | `dotnet test tests\MUnique.OpenMU.Tests\MUnique.OpenMU.Tests.csproj` nesta rodada |
| Working tree | Somente `.codex/config.toml` não rastreado (preservado) | `git status --short` |
| Banco homologação | `openmu_cashshop_validation` (PostgreSQL 15 local, daemon Docker parado) | `docs/plans/CashShop_Homologacao_Banco_e_Seed.md`; inspeção `information_schema`/`pg_catalog` read-only nesta rodada |

Os valores da Fase 9 reportados antes desta rodada foram confirmados sem divergência.

## 1. Protocolo D2 0E

### 1.1 Direção — correção em relação ao documento da Fase 9

`docs/cashshop/phase-09-storage-final.md` lista "D2 0E | cliente → servidor". **Isto
está incorreto.** Evidência:

- No MuMain, `0x0E` é tratado no **dispatcher de recebimento** do grupo `0xD2`
  (`WSclient.cpp:14442` `case 0xD2:` → `WSclient.cpp:14480-14482`
  `case 0x0E: ReceiveIGS_StorageGiftItemList(ReceiveBuffer);`).
- Não existe nenhuma função de envio com subcode `0x0E` no cliente: os envios
  cash shop vão pela ClientLibrary gerenciada (`PacketBindings_ClientToServer.h:224-246`,
  `PacketFunctions_ClientToServer.cpp:356-388`) e cobrem apenas 01 (point info),
  02 (open state), 03 (buy), 04 (gift), 05 (storage list), 0A (delete), 0B (consume), 13 (event list).

**D2 0E é servidor → cliente.** A linha da Fase 9 deve ser corrigida na próxima
atualização documental (não corrigida nesta rodada — documento alheio preservado).

### 1.2 Struct do cliente (CONFIRMADO)

`C:\MuMain\src\source\Network\Server\WSclient.h:3325-3339`:

```c
// (0xD2)(0x0E)
typedef struct
{
    PBMSG_HEADER2       h;
    long                lStorageIndex;
    long                lItemSeq;
    long                lStorageGroupCode;
    long                lProductSeq;
    long                lPriceSeq;
    double              dCashPoint;
    char                chItemType;
    char                chSendUserName[MAX_USERNAME_SIZE + 1];
    char                chMessage[MAX_GIFT_MESSAGE_SIZE];
} PMSG_CASHSHOP_GIFTSTORAGELIST, * LPPMSG_CASHSHOP_GIFTSTORAGELIST;
```

Constantes: `MAX_USERNAME_SIZE = 10` (`Core\Globals\_define.h:316`) e
`MAX_GIFT_MESSAGE_SIZE = 200` (`Core\Globals\_define.h:339`).
`PBMSG_HEADER2` = `PBMSG_HEADER {BYTE Code; BYTE Size; BYTE HeadCode}` + `BYTE m_bySubCode`
(`WSclient.h:2240-2244`, `WSclient.h:108-114`) = **4 bytes**, byte-aligned
(o bloco cash shop está em região `#pragma pack(push,1)` — ver observação equivalente já
homologada para D2 0D em `CashShopConnectionExtensions.cs:14-30`).

### 1.3 Campos, offsets e tamanho de fio

Total: 4 + 20 + 8 + 1 + 11 + 200 = **244 bytes**. Cabe em header C1 (byte de length 244 ≤ 255).

| Campo | Offset | Bytes | Tipo | Evidência |
|---|---|---|---|---|
| `h` (C1 header + subcode) | 0 | 4 | `[0]=0xC1, [1]=244, [2]=0xD2, [3]=0x0E` | WSclient.h:2240-2244,3327; dispatcher WSclient.cpp:14442,14480 |
| `lStorageIndex` | 4 | 4 | int32 LE | WSclient.h:3329 |
| `lItemSeq` | 8 | 4 | int32 LE | WSclient.h:3330 |
| `lStorageGroupCode` | 12 | 4 | int32 LE | WSclient.h:3331 |
| `lProductSeq` | 16 | 4 | int32 LE | WSclient.h:3332 |
| `lPriceSeq` | 20 | 4 | int32 LE | WSclient.h:3333 |
| `dCashPoint` | 24 | 8 | double LE (não alinhado) | WSclient.h:3334; pack(1) |
| `chItemType` | 32 | 1 | char (`'P'`/`'C'`) | WSclient.h:3335 |
| `chSendUserName` | 33 | 11 | UTF-8, NUL-padded (`char[11]`) | WSclient.h:3337; `_define.h:316` |
| `chMessage` | 44 | 200 | UTF-8, NUL-padded (`char[200]`) | WSclient.h:3338; `_define.h:339` |

### 1.4 Recebimento e encoding (CONFIRMADO)

`C:\MuMain\src\source\Network\Server\WSclient.cpp:12673-12690` —
`ReceiveIGS_StorageGiftItemList`:

```c
wchar_t szID[MAX_USERNAME_SIZE + 1];
wchar_t szMessage[MAX_GIFT_MESSAGE_SIZE];
CMultiLanguage::ConvertFromUtf8(szID, Data->chSendUserName, MAX_USERNAME_SIZE);
CMultiLanguage::ConvertFromUtf8(szMessage, Data->chMessage, MAX_GIFT_MESSAGE_SIZE);
g_pInGameShop->AddStorageItem(..., (char)Data->chItemType, szID, szMessage);
```

- **Encoding: UTF-8 → UTF-16** (`MultiLanguage.cpp:58-97`; `MultiByteToWideChar(CP_UTF8, ...)`).
  A chamada usa `maxSourceLength = 10` para o nome e `200` para a mensagem.
- **Terminador NUL (evidência crítica, `MultiLanguage.cpp:58-97`)**: a função lê
  exatamente `maxSourceLength` bytes; só acrescenta `L'\0'` quando
  `written < maxSourceLength`. Em seguida `AddStorageItem` faz `wcscpy`
  (`NewUIInGameShop.cpp:777,786`) dos buffers de 11/200 wchars. **Se o servidor
  preencher os 200 bytes (ou os 11 do nome) sem nenhum NUL dentro do campo, o
  `wcscpy` lê além do buffer (UB).** Regra resultante para o servidor:
  o campo `chMessage` deve conter no máximo **199 bytes codificados + pelo menos 1 NUL**;
  `chSendUserName` no máximo **10 bytes + pelo menos 1 NUL** (o cliente converte o nome
  com limite 10, então bytes além disso nem são lidos).
- `AddStorageItem` com remetente/mensagem: `NewUIInGameShop.cpp:754-787` — grava
  `Item.m_szSendUserName` / `Item.m_szMessage` em `IGS_StorageItem`
  (`UI\Legacy\UIControls.h:1104-1123`; buffers 11/200 wchars).
  `pszUserName == NULL` → string vazia (legado sem metadados exibido sem remetente).

### 1.5 Apresentação na UI (CONFIRMADO)

`C:\MuMain\src\source\GameShop\NewUIInGameShop.cpp:420-449` — o botão "Use" escolhe o
dialog pela aba ativa:

- Aba `IGS_SAFEKEEPING_LISTBOX = 0` (compras) → `CMsgBoxIGSStorageItemInfo` **sem** remetente/mensagem.
- Aba `IGS_PRESENTBOX_LISTBOX = 1` (presentes) → `CMsgBoxIGSGiftStorageItemInfo::Initialize(...)`
  (`MsgBoxIGSGiftStorageItemInfo.cpp:85-108`): renderiza `ItSAGiftFromS` ("é um presente
  de %ls") com `pszID` e carrega a mensagem em `m_MessageInputBox`
  (`SetTextLimit(MAX_GIFT_MESSAGE_SIZE)`, `MsgBoxIGSGiftStorageItemInfo.cpp:66,107`).

### 1.6 Diferença D2 0D × D2 0E (CONFIRMADO)

| | D2 0D (`PMSG_CASHSHOP_STORAGELIST`) | D2 0E (`PMSG_CASHSHOP_GIFTSTORAGELIST`) |
|---|---|---|
| Struct | WSclient.h:3309-3320 | WSclient.h:3325-3339 |
| Tamanho | 33 bytes | 244 bytes |
| Campos | 5 longs + double + char | idênticos + `chSendUserName[11]` + `chMessage[200]` |
| Dispatcher | `case 0x0D` (WSclient.cpp:14477-14478) | `case 0x0E` (WSclient.cpp:14480-14482) |
| Handler | `ReceiveIGS_StorageItemList` → `AddStorageItem` sem nome/mensagem | `ReceiveIGS_StorageGiftItemList` → `AddStorageItem` com nome/mensagem |
| Aba | compras ('S') | presentes ('G') |

### 1.7 Suporte do dispatcher (CONFIRMADO)

O cliente atual **suporta** D2 0E: o `case 0x0E` está compilado no dispatcher
(WSclient.cpp:14480-14482), sem `#ifdef` que o desligue no trecho relevante.

## 2. Fluxo Gift atual (CONFIRMADO, arquivos/métodos exatos)

```text
D2 04 (C1/D2/0x04, 234 bytes)
   ↓  CashShopItemGiftRequestHandlerPlugIn.HandlePacketAsync
   ↓     (GameServer\MessageHandler\CashShop\CashShopItemGiftRequestHandlerPlugIn.cs:47-75;
   ↓      lê request.GiftReceiverName @23/11B e request.GiftText @34/200B —
   ↓      offsets em ClientToServerPackets.cs:6400-6413)
   ↓  CashShopGiftAction.HandleGiftAsync
   ↓     (GameLogic\PlayerActions\CashShop\CashShopGiftAction.cs:44-73 — repassa giftText sem usar)
   ↓  CashShopGiftService.ProcessGiftAsync
   ↓     (GameLogic\PlayerActions\CashShop\CashShopGiftService.cs:91-271)
   ├─ receptor: GetAccountByCharacterNameAsync(receiver)  (linha 147-149)
   ├─ slot:    _storageService.ReserveNextStorageIndexAsync(..., forGift: true)
   │           → context.CreateNew<CashShopStorageItem>()  (CashShopStorageService.cs:130-183)
   ├─ item:    player.PersistenceContext.CreateNew<Item>() + storageItem.Item = item (198-207)
   ├─ débito:  WCoinService.TryDebit(...)  (209-214)
   └─ commit:  player.PersistenceContext.SaveChangesAsync()  (221)
   ↓
CashShopStorageItem (data."CashShopStorageItem", PostgreSQL)
   ↓
D2 05 → CashShopStorageListAction.RequestListAsync
          (GameLogic\PlayerActions\CashShop\CashShopStorageListAction.cs:62-122)
       aba 'G': GetGiftCountAsync / GetGiftPageAsync
          (GameLogic\CashShop\CashShopStorageService.cs:59-63, 83-96, 197-209)
       → view: ShowStorageItemAsync → SendCashShopStorageItemAsync (**D2 0D**, 33 bytes)
          (GameServer\RemoteView\CashShop\CashShopViewPlugIn.cs:48-52;
           Network\Packets\ServerToClient\CashShopConnectionExtensions.cs:158-192)
```

Respostas às perguntas da etapa:

1. **Onde `GiftText` é recebido?** `CashShopItemGiftRequestHandlerPlugIn.HandlePacketAsync`
   (linha 73: `request.GiftText`), extraído do offset 34, 200 bytes UTF-8.
2. **Onde é descartado?** Em `CashShopGiftService.ProcessGiftAsync`: o parâmetro
   `giftText` (linha 100) **não é usado em nenhuma linha do corpo**; o doc comment
   (linhas 47-61) registra a decisão ("aceito e descartado"). `CashShopGiftAction`
   apenas repassa o parâmetro.
3. **O serviço conhece o remetente autenticado?** Sim: `player.SelectedCharacter` /
   `player.Account` (linhas 104-107) e `player.Name` (= `SelectedCharacter?.Name`,
   `GameLogic\Player.cs:206`) usado só no log (linhas 253-258). **Nada do remetente é
   persistido.**
4. **Nome esperado no D2 0E: conta ou personagem?** **Personagem.** Evidências: o
   presente é endereçado por *nome de personagem* no D2 04 (resolvido em
   `GetAccountByCharacterNameAsync`, CashShopGiftService.cs:147); o campo tem
   `MAX_USERNAME_SIZE + 1` = 11 bytes, o mesmo formato de nome de personagem/login
   usado em todo o cliente; a UI renderiza "é um presente de %ls" — identidade de
   jogador. `MAX_USERNAME_SIZE = 10` coincide com o limite de nome do cliente.
5. **Como o presente é vinculado ao destinatário?** A linha de storage é criada com
   `AccountId` do destinatário (`ReserveNextStorageIndexAsync(player.PersistenceContext,
   receiverAccount.Id, forGift: true)`, CashShopGiftService.cs:184-186) e `IsGift = true`
   (CashShopStorageService.cs:168).
6. **Onde ocorre o débito WCoin?** `WCoinService.TryDebit` — CashShopGiftService.cs:209-214,
   sobre a conta do **remetente** (`account.Id`), com `WCoinTransactionType.Purchase`.
7. **Onde ocorre o `SaveChangesAsync`?** CashShopGiftService.cs:221 — **um único**
   commit para débito + item + linha de storage (+ metadados, quando existirem).
8. **Storage diferencia Purchase e Gift por `IsGift`?** Sim — `CashShopStorageItem.IsGift`
   (`DataModel\Entities\CashShopStorageItem.cs:138`), migration
   `20260927120000_AddCashShopStorageGiftFlag`.
9. **Como a aba Gift é listada hoje?** `CashShopStorageListAction.RequestListAsync`
   com `inventoryType == 'G'` (linha 73) → `GetGiftCountAsync`/`GetGiftPageAsync`
   (filtro `IsGift == true` e `AccountId`, ordenado por `StorageIndex`,
   CashShopStorageService.cs:204-208) → `ShowStorageItemAsync` por item (linhas 107-121).
10. **A listagem Gift usa D2 0D?** **Sim.** A view não tem método para D2 0E;
    `CashShopViewPlugIn.ShowStorageItemAsync` → `SendCashShopStorageItemAsync`
    (D2 0D, 33 bytes) é usado nas **duas** abas. `CashShopConnectionExtensions.cs`
    **não contém nenhum serializer D2 0E** (varredura completa do arquivo, 359 linhas).

Contexto adicional do serviço de storage (relevante para a Fase 10):

- `Capacity = PageSize = 30` (CashShopStorageService.cs:35,49).
- `ItemSeq` é sequência **global por conta nas duas abas** (`max(ItemSeq)+1`,
  CashShopStorageService.cs:174-175) — o D2 0B não informa a aba; isso **não pode mudar**.
- Unicidade por aba `(AccountId, IsGift, StorageIndex)` + índice `(AccountId, IsGift)`
  (`CashShopStorageModelBuilderExtensions.cs:30-33`).

## 3. PostgreSQL e modelo de dados (CONFIRMADO — inspeção read-only)

### 3.1 Schema atual de `data."CashShopStorageItem"` (lido em `openmu_cashshop_validation`)

| Coluna | Tipo PG | Null | Default |
|---|---|---|---|
| `Id` | uuid | NO | — |
| `ItemId` | uuid | YES | — (FK → `data."Item"` ON DELETE CASCADE) |
| `AccountId` | uuid | NO | — (FK → `data."Account"` ON DELETE CASCADE) |
| `StorageIndex` | integer | NO | — |
| `ItemSeq` | integer | NO | — |
| `StorageGroup` | integer | NO | — |
| `ProductSeq` | integer | NO | — |
| `PriceSeq` | integer | NO | — |
| `CashPoint` | double precision | NO | — |
| `ItemType` | varchar(1) | NO | — |
| `PurchasedAt` | timestamptz | NO | — |
| `IsGift` | boolean | NO | `false` |

Constraints: PK (`Id`) + 2 FKs (Account, Item). Índices: `IX_..._AccountId`,
`IX_..._AccountId_IsGift`, único `(AccountId, IsGift, StorageIndex)`, único `ItemId`.
Migrations aplicadas: 56, última `20260927120000_AddCashShopStorageGiftFlag`
(= estado da Fase 9). Dados atuais: 1 linha, 0 presentes (base de homologação recém-criada).

**Não existem colunas para remetente ou mensagem.** Entidade C# correspondente:
`CashShopStorageItem` (`DataModel\Entities\CashShopStorageItem.cs:37-144`) — campos
`Id, AccountId, StorageIndex, ItemSeq, StorageGroup, ProductSeq, PriceSeq, CashPoint,
ItemType, Item, IsGift, PurchasedAt`. Model builder: `CashShopStorageModelBuilderExtensions.cs`.

### 3.2 Campos propostos (proposta — **migration NÃO criada/executada nesta rodada**)

| Campo | Tipo C# | Tipo PostgreSQL | Limite | Nullable | Justificativa |
|---|---|---|---|---|---|
| `GiftSender` | `string?` | `character varying(10)` | 10 chars (`HasMaxLength(10)` no model builder) | SIM | Nome do **personagem** remetente (ver §2.4). 11 bytes no fio = 10 chars + NUL; nomes do cliente são limitados a 10 (`MAX_USERNAME_SIZE`). Nullable: presentes antigos e compras não têm remetente. |
| `GiftMessage` | `string?` | `character varying(200)` | 200 chars (`HasMaxLength(200)`) | SIM | Mensagem do presente. No fio cabem ≤199 bytes codificados + NUL (§1.4); chars ≤ bytes em UTF-8, então 200 chars de coluna nunca estouram o que o fio pode carregar. Nullable pelos mesmos motivos. |

Regras da proposta (a implementar na Fase 10, não agora):

- Sem default e sem `NOT NULL`: nenhuma linha existente é reescrita (`AddColumn` puro).
- Sem alteração de índices, constraints, FKs, `ItemSeq`, `StorageIndex`, `ItemId`, `IsGift`.
- No model builder: `HasMaxLength` apenas — nenhuma mudança de relacionamento.
- Compatibilidade retroativa: linhas antigas com `NULL` nos dois campos; o serializer
  D2 0E trata nulo como campo vazio (NUL-padded), que é exatamente o caminho
  `pszUserName == NULL` do cliente (`NewUIInGameShop.cpp:771-783`).
- O Claim D2 0B não é afetado: resolve por `(AccountId, ItemSeq, StorageIndex,
  ItemType='P')` e os novos campos não participam da resolução.
- Persistência **no mesmo commit** do presente (§4.3).

## 4. Mensagens, encoding e compatibilidade

- **Encoding no fio: UTF-8** — CONFIRMADO em ambos os sentidos: o request D2 04
  decodifica `GiftText` com UTF-8 (`ClientToServerPackets.cs:6411`); o cliente converte
  a resposta com `MultiByteToWideChar(CP_UTF8, ...)` (MultiLanguage.cpp:66,74).
  Nada presume outra página de código.
- **Mensagem vazia**: persistir `GiftMessage = null` (ou string vazia → escolha de
  implementação: nulo). No fio: campo 200 bytes zerados. O cliente converte e mostra
  input box vazio; remetente vazio mostra `ItSAGiftFromS` com id vazio — aceitável e
  igual ao comportamento do cliente para `NULL`.
- **Acentos / pt-BR**: "ç", "ã" etc. = 2 bytes UTF-8. Uma mensagem de 150 chars com
  acentos pode passar de 199 bytes: o truncamento deve ser **por bytes codificados**.
- **Truncamento seguro** (proposta): codificar a string para UTF-8, cortar em ≤199
  bytes **recuando até fronteira de caractere** (nunca dividir sequência multibyte —
  `MultiByteToWideChar` sem `MB_ERR_INVALID_CHARS` substitui bytes inválidos por U+FFFD,
  o que produz lixo visível), depois NUL-padear até 200. Idem remetente: ≤10 bytes
  (nomes são validados na criação do personagem; caso extremo, truncar igual).
  `WriteString` atual (`ByteSpanExtensions.cs:133-137`) zera o span e lança se não
  couber — o truncamento tem que acontecer **antes** do `WriteString`.
- **Conteúdo malformado** (NUL no meio, bytes inválidos): `ExtractString` para no
  primeiro NUL (ByteSpanExtensions.cs:110-125); persistir o que sobrar válido.
- **Legados sem nome/mensagem**: colunas NULL → campos vazios no fio. **Não inventar
  remetente fictício** (nem "Sistema", nem o próprio destinatário): o cliente mostra
  "presente de ''" — determinístico e honesto. A atribuição de um remetente sintético
  contaminaria o dado real.
- **Privacidade**: não logar o conteúdo da mensagem (já é a postura do serviço:
  logs citam produto/valor/receptor, nunca o texto — CashShopGiftService.cs:253-258).

## 5. Segurança e atomicidade (auditoria da Fase 10 proposta)

| Risco | Situação proposta | Evidência |
|---|---|---|
| Spoofing do remetente | Eliminado por construção: o D2 04 **não carrega** campo de remetente (struct em ClientToServerPackets.cs:6286-6428: PackageMainIndex, Category, ProductMainIndex, ItemIndex, CoinIndex, MileageFlag, GiftReceiverName, GiftText). O remetente deve ser derivado de `player.SelectedCharacter.Name` (identidade autenticada, Player.cs:206) **dentro do serviço**, nunca de um parâmetro do pacote. | CashShopGiftService.cs:104-107 |
| Exposição de presente de outra conta | Listagem filtra `AccountId == account.Id` e a aba por `IsGift` (CashShopStorageService.cs:204-208); `RequestListAsync` só usa `player.Account` (StorageListAction.cs:64). O D2 0E deve ser emitido apenas para linhas da própria conta — o mesmo escopo do D2 0D atual. | idem |
| Persistência parcial da mensagem | Metadados vão em `storageItem.GiftSender/GiftMessage` **antes** do único `SaveChangesAsync` (linha 221) — mesma transação do débito e do item. Nenhum commit extra. | CashShopGiftService.cs:192-225 |
| Rollback | `DiscardStagedGiftAsync` reverte débito + `Detach` da linha e do item (338-390). Como os metadados são propriedades da mesma linha, o `Detach(storageItem)` os descarta juntos; falha de rollback aposenta a sessão (`UnsafeContextRetirement`), comportamento já homologado. | CashShopGiftService.cs:303-390 |
| Mensagem no StorageItem errado | Os metadados são atribuídos ao objeto devolvido pela reserva (`reservation.StorageItem`) — o mesmo objeto que recebe `StorageGroup/ProductSeq/...` (linhas 202-207). Sem nova consulta. | CashShopGiftService.cs:184-207 |
| Impacto no `ItemSeq` global | Nenhum: novas colunas não participam de `ReserveNextStorageIndexAsync` (alocação `max(ItemSeq)+1` por conta, CashShopStorageService.cs:174-175) nem da unicidade por aba. | idem |
| Compatibilidade com D2 0B | Claim resolve por `(AccountId, ItemSeq, StorageIndex, ItemType='P')` e não toca nos novos campos; presentes continuam com `ItemType='P'` e `IsGift=true`. O D2 0B **não muda**. | CashShopStorageClaimService.cs (fluxo homologado na Fase 9) |

Garantias obrigatórias preservadas: 1 débito WCoin por gift; 1 `CashShopStorageItem`;
metadados no mesmo commit; storage somente da conta destinatária; claim com mesmo
`ItemId`, sem segundo débito, sem duplicação, sem perda de item.
**Nenhum refactor de `GiftService`/`ClaimService` além do mínimo** (preencher os dois
campos na entrega) deve ser feito na Fase 10.

## 6. Testes

### 6.1 Inventário existente (todos GREEN no run desta rodada — 640/640)

| Arquivo | Cobertura |
|---|---|
| `tests\MUnique.OpenMU.Tests\CashShop\CashShopGiftServiceTests.cs` | rejeição de não-giftable, flag 184, quantity 20 no storage item, rollback incompleto/cancelamento com retirement |
| `...\CashShopStorageServiceTests.cs` | índices/paginação por conta, abas separadas (`GiftAndPurchaseRows_AreListedInSeparateTabs`), reserva por aba, slots |
| `...\CashShopStorageListActionTests.cs` | aba S com contagem + itens account-scoped, aba G só com gift rows, storage type desconhecido, página fora do range |
| `...\CashShopStorageClaimTests.cs` | claim válido, inventário cheio, índice de outra conta, dupla claim, gift row preserva quantity e não debita |
| `tests\MUnique.OpenMU.Network.Packets.Tests\CashShopPacketTests.cs` | layout byte-a-byte de D2 01/02/06/03/0D/0B/0C/13/15 (padrão para o RED do D2 0E — ver `CashShopStorageItem_HasCorrectLayout`, linhas 190-219) |
| `tests\MUnique.OpenMU.Persistence.Initialization.Tests\CashShopStorageItemMigrationTests.cs` | persistência/migration da entidade |
| `...\UnsafeContextRetirementTests.cs` | aposentadoria de sessão em rollback incompleto |

### 6.2 Testes RED planejados (só o plano — nada implementado nesta rodada)

1. **Presente com remetente/mensagem**: gift de A para B → linha de B persiste
   `GiftSender = A.SelectedCharacter.Name` e `GiftMessage` integral (dentro do limite).
2. **Remetente derivado da sessão**: mesmo com o pacote malicioso, nenhum campo do
   pacote altera o remetente (não há campo; provar que a origem é `player.SelectedCharacter.Name`).
3. **Mensagem vazia**: gift com `GiftText` vazio/só NULs → `GiftMessage` nula, gift entregue.
4. **Mensagem com acentos**: "Olá! Presente de Natal ção" → persiste íntegra; fio UTF-8 correto.
5. **Mensagem no limite de bytes**: mensagem cuja codificação UTF-8 tem exatamente
   199 bytes → persistida integral; fio com NUL no byte 200.
6. **Mensagem acima do limite**: codificação >199 bytes → truncada em fronteira de
   caractere, sem sequência UTF-8 quebrada, sem exceção de `WriteString`.
7. **Gift antigo sem metadados**: linha legada (`GiftSender/GiftMessage` NULL) → D2 0E
   com campos vazios NUL-padded; nenhuma exceção; sem remetente inventado.
8. **Serialização D2 0E**: `SendCashShopGiftStorageItemAsync` → 244 bytes, header
   `C1/244/D2/0E`, cada campo no offset da tabela §1.3 (espelhar
   `CashShopStorageItem_HasCorrectLayout`).
9. **Offsets/tamanho**: casos de fronteira (mensagem 0/1/199 bytes; remetente 10 chars).
10. **Gift tab envia D2 0E**: `RequestListAsync('G')` → N linhas D2 0E com
    remetente/mensagem das linhas `IsGift`.
11. **Purchase tab continua D2 0D**: `RequestListAsync('S')` → só D2 0D, nenhum 0E.
12. **Paginação e D2 06 preservados**: contadores/páginas idênticos ao comportamento
    atual nas duas abas.
13. **Account-scoped Gift**: gift de outra conta nunca aparece na listagem/0E da conta.
14. **Rollback sem metadados órfãos**: falha pós-staging → `Detach` da linha descarta
    sender/message junto (nenhuma linha com metadados sem item/débito).
15. **Gift → Claim sem alteração de ItemId/WCoin**: após Fase 10, claim do presente
    com metadados continua entregando o mesmo `ItemId` com delta WCoin = 0.

### 6.3 GREEN obrigatório

- Suíte completa `MUnique.OpenMU.Tests` 640/640 (baseline desta rodada).
- `MUnique.OpenMU.Network.Packets.Tests` (inclui os novos layouts).
- `MUnique.OpenMU.Persistence.Initialization.Tests` (migration aditiva).
- Homologação PostgreSQL (`tools/cashshop-homologation`), com cenário novo gift+mensagem.

## 7. Plano de implementação proposto (não executado)

| # | Etapa | Arquivos envolvidos | Riscos |
|---|---|---|---|
| 1 | Testes RED (§6.2) | `tests\MUnique.OpenMU.Network.Packets.Tests\CashShopPacketTests.cs`; `tests\MUnique.OpenMU.Tests\CashShop\*` |nenhum — RED por falta do serializer/colunas |
| 2 | Modelo + migration aditiva | `src\DataModel\Entities\CashShopStorageItem.cs`; `src\Persistence\EntityFramework\Extensions\ModelBuilder\CashShopStorageModelBuilderExtensions.cs`; nova migration em `src\Persistence\EntityFramework\Migrations\` | baixo: colunas nullable, sem default; **não** mexer em índices/uniqueness |
| 3 | Preservar remetente/mensagem no GiftService | `src\GameLogic\PlayerActions\CashShop\CashShopGiftService.cs` (+ interface `ICashShopGiftService` se a assinatura mudar) | médio: tocar no fluxo homologado — mudança mínima (2 atribuições antes do único commit), origem do remetente só em `player.SelectedCharacter.Name` |
| 4 | Serializer D2 0E | `src\Network\Packets\ServerToClient\CashShopConnectionExtensions.cs` | truncamento por bytes + NUL-padding obrigatório (§1.4); nunca passar string sem truncar para `WriteString` |
| 5 | Integração na aba Gift | `src\GameLogic\Views\CashShop\ICashShopViewPlugIn.cs` (novo método); `src\GameServer\RemoteView\CashShop\CashShopViewPlugIn.cs`; `src\GameLogic\PlayerActions\CashShop\CashShopStorageListAction.cs` | médio: mudança de interface da view afeta test doubles; aba 'S' **não** pode passar a enviar 0E |
| 6 | Testes CashShop GREEN | idem etapa 1 | rodar alvo + suíte completa |
| 7 | Suíte completa | `tests\MUnique.OpenMU.Tests` | 640+GREEN; regressão zero |
| 8 | Homologação PostgreSQL | `tools\cashshop-homologation`; banco `openmu_cashshop_validation` | aplicar migration; revisar cenários com mensagem/acentos/truncamento |
| 9 | Validação MuMain | apenas leitura de `C:\MuMain` | cliente não muda; validar exibição "presente de X" + mensagem |
| 10 | Documentação | `docs\cashshop\phase-10-gift-storage-final.md`; corrigir direção D2 0E em `phase-09-storage-final.md` | — |
| 11 | Commits e tag | git | seguir padrão `feat/test/docs(cashshop)` + tag `cashshop-phase10-complete` |

Ordem ajustada em relação ao rascunho original: a migration (2) **antes** do
serializer (4) evita RED duplo no projeto de persistência; a interface da view (5)
deve mudar junto com o serializer (4) para manter os testes de layout e de listagem
no mesmo RED/GREEN.

## 8. Riscos consolidados

1. **UB no cliente** se algum campo sair sem NUL interno (§1.4) — mitigação: truncar
   em 199/10 bytes codificados + NUL-pad; testar exatamente os limites.
2. **`WriteString` lança** com texto maior que o campo — truncar antes, por bytes.
3. **Regressão na aba 'S'** se o dispatcher da view enviar 0E indevidamente — teste 11.
4. **`ItemSeq`/claim** — nenhum toque na alocação nem no claim; testes 13-15 cobrem.
5. **Dados legados** — colunas nullable + campos vazios no fio; sem valores default
   fabricados.
6. **Documento da Fase 9** com direção errada de D2 0E — corrigir na Fase 10 (risco
   apenas documental).

## 9. Hipóteses ainda não validadas (DESCONHECIDO / PROVÁVEL)

- **PROVÁVEL**: `ConvertFromUtf8` com NUL embutido no meio do campo converte os NULs
  como `L'\0'` e o `wcscpy` para no primeiro — o comportamento de padding-NUL do
  servidor é seguro (derivado do código de `MultiLanguage.cpp` + `wcscpy`; não
  executado no cliente real).
- **DESCONHECIDO**: comportamento do cliente com remetente contendo caracteres
  multibyte (nomes válidos deste servidor são ASCII; o teste de acento cobre a
  mensagem, não o nome).
- **DESCONHECIDO**: se algum build do Main.exe em produção não tenha o `case 0x0E`
  compilado (o MuMain de referência tem; Main.exe não foi disassemblado nesta rodada —
  fora do escopo).
