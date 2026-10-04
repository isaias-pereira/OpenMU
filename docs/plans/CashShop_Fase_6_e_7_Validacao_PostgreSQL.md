# CashShop/XShop — Fase 6 validada e Fase 7 consolidada

**Data:** 2026-09-29
**Branch:** `cashshop`
**HEAD no início desta sessão:** `56cf0725e` (`fix(game-logic): fix custom move alias resolution and command normalization`), um commit à frente do referencial `265b22009a`.
**Escopo:** validar a Fase 6 em PostgreSQL isolado e consolidar o estado real da Fase 7.

> Este documento é o registro **verificado** desta sessão. O documento `CashShop_Status_Pre_Fase_7.md`
> permanece como histórico do estado anterior e **não** foi alterado.
>
> **Regra aplicada:** nada aqui é afirmado apenas por compilação. Cada afirmação sobre o banco cita o
> comando executado e o resultado observado.

## 1. Estado do repositório no início

- Branch `cashshop`, HEAD `56cf0725e`.
- 71 entradas em `git status` (modificados + não rastreados) — **preservadas integralmente**.
- Nenhuma operação destrutiva (`reset`, `clean`) foi executada.
- O índice de busca do ambiente estava desatualizado (não indexava os `.cs` do projeto), então toda a
  investigação foi feita com `Select-String` e leitura direta de arquivos.

## 2. Diagnóstico: o que já existia

A premissa da tarefa ("a Fase 7 não está integrada ponta a ponta") **não corresponde mais ao código**.
Verifiquei item a item e a maior parte da Fase 7 já estava implementada:

| Item | Estado encontrado |
|---|---|
| Struct C2S `CashShopItemBuyRequest` (D2 03) | **EXISTE** — `ClientToServerPackets.cs`, 23 bytes, subcódigo `0x03` |
| Resposta S2C D2 03 | **EXISTE** — `SendCashShopBuyResultAsync`, 9 bytes |
| Handler de entrada D2 03 | **EXISTE** — `CashShopItemBuyRequestHandlerPlugIn` |
| Grupo D2 e registro em DI | **EXISTEM** — `CashShopGroupHandlerPlugIn` + `Program.cs` |
| Serviço de compra | **EXISTE** — `CashShopBuyService` |
| Entrega | **EXISTE** — `CashShopStorageItem` (storage da conta) |
| Débito de moeda | **EXISTE** — `WCoinService.TryDebit` |
| Rollback | **EXISTE** — `DiscardStagedPurchaseAsync` / `RevertDebitAsync` |
| Serialização por conta | **EXISTE** — `ConcurrentDictionary<Guid, AsyncLock>` |

## 3. Fase 6 — validação real em PostgreSQL

### 3.1 Ambiente

- PostgreSQL **15.18** local (`postgresql-x64-15`, em execução).
- Docker instalado, mas o **daemon está parado** — não foi usado.
- **O banco `openmu` (desenvolvimento) NÃO foi alterado.** Ele tem 35 tabelas, as 4 tabelas de catálogo
  vazias e 4 migrations de Cash Shop pendentes. Para não tocar produção, criei um banco separado:
  `openmu_cashshop_validation`.

### 3.2 Migrations

Comando (executado na pasta do projeto EntityFramework):

```
dotnet ef database update --context EntityDataContext --connection "Server=localhost;Port=5432;User Id=postgres;Password=postgres;Database=openmu_cashshop_validation;Command Timeout=120;"
```

**Resultado: `Done.` — todas as migrations aplicaram sem erro.** A ordem observada inclui:

- `20260919172300_AddCashShopCatalog`
- `20260922034640_RegenerateSnapshot`
- `20260923062714_GrantCashShopConfigRole`
- `20260925085448_FixCashShopCatalogNaturalKeys`
- `20260926174815_AddCashShopStorage`
- `20260927120000_AddCashShopStorageGiftFlag`

**Idempotência:** reexecutei o mesmo comando e a resposta foi `No migrations were applied. The database
is already up to date.` O `__EFMigrationsHistory` ficou com **56 linhas**, sem duplicatas.

### 3.3 Schema resultante

Tabelas de Cash Shop em `openmu_cashshop_validation` (5): `CashShopCategory`, `CashShopPackage`,
`CashShopPackageProduct`, `CashShopProduct`, `CashShopStorageItem`.

Índices de `CashShopStorageItem` (confirmados em `pg_indexes`):

```
CREATE UNIQUE INDEX "PK_CashShopStorageItem" ON data."CashShopStorageItem" USING btree ("Id")
CREATE INDEX "IX_CashShopStorageItem_AccountId" ON data."CashShopStorageItem" USING btree ("AccountId")
CREATE UNIQUE INDEX "IX_CashShopStorageItem_ItemId" ON data."CashShopStorageItem" USING btree ("ItemId")
CREATE UNIQUE INDEX "IX_CashShopStorageItem_AccountId_IsGift_StorageIndex" ON data."CashShopStorageItem" USING btree ("AccountId", "IsGift", "StorageIndex")
CREATE INDEX "IX_CashShopStorageItem_AccountId_IsGift" ON data."CashShopStorageItem" USING btree ("AccountId", "IsGift")
```

**9 foreign keys** de Cash Shop. Em `CashShopStorageItem`:

```
FK_CashShopStorageItem_Account_AccountId -> data."Account"("Id") ON DELETE CASCADE
FK_CashShopStorageItem_Item_ItemId       -> data."Item"("Id")    ON DELETE CASCADE
```

### 3.4 Seed do catálogo

**Limitação honesta:** o seed real exige os arquivos IBS (`IBSCategory.txt`, `IBSPackage.txt`,
`IBSProduct.txt`), que **não existem no repositório**, e ele roda em processo através do
`ConnectionConfigurator`, que é singleton e já está preso ao banco `openmu`. Redirecioná-lo in-process
para o banco de validação não é possível sem alterar a infraestrutura de conexão.

Por isso validei a **camada de banco** do seed inserindo as mesmas entidades que
`CashShopCatalogInitializer` grava, dentro de transação, e consultando o resultado:

```
 price_server_side | availability_flag
-------------------+-------------------
               500 |               144

 package_cash_type | resolved_price
-------------------+----------------
               509 |            500
```

Isso confirma, no PostgreSQL real: o **preço vem do banco** (500), o **tipo de moeda vem do pacote**
(509) e o **flag de disponibilidade** é gravado (144). Também observei que
`CashShopPackageProduct` é chaveado por `(PackageId, ProductId, Order)` — não por
`(ProductSeq, PriceSeq)`.

O **comportamento idempotente do inicializador C#** está coberto por teste unitário com
`InMemoryContext`; a unicidade no PostgreSQL real não foi exercitada pelo inicializador por causa do
bloqueio descrito acima.

## 4. Fase 7 — o que foi verificado no banco

### 4.1 Saldo negativo é barrado pelo banco

```sql
UPDATE data."WCoinWallet" SET "Balance" = -50 WHERE "Id"=...;
```

**Resultado observado:**

```
ERROR:  new row for relation "WCoinWallet" violates check constraint "CK_WCoinWallet_Balance"
DETAIL:  Failing row contains (..., -50, ...)
```

### 4.2 Entrega duplicada no mesmo slot é barrada pelo banco

Segundo insert em `(AccountId, IsGift, StorageIndex)` já ocupado:

```
ERROR:  duplicate key value violates unique constraint "IX_CashShopStorageItem_AccountId_IsGift_StorageIndex"
DETAIL:  Key ("AccountId", "IsGift", "StorageIndex")=(..., f, 0) already exists.
```

Este é o mecanismo que impede duas compras concorrentes tomarem o mesmo slot de entrega, mesmo entre
processos diferentes.

### 4.3 Atomicidade

O código faz **um único** `player.PersistenceContext.SaveChangesAsync` para gravar a linha de storage,
o `Item` entregue e o débito de WCOIN (`CashShopBuyService`). O EF Core envolve a unidade de trabalho em
uma transação. Em caso de falha, `DiscardStagedPurchaseAsync` reverte wallet + extrato + linhas de
entrega, e um rollback incompleto resulta em `RollbackIncomplete` **e** na retirada da sessão.

### 4.4 Preço nunca vem do cliente

O request D2 03 carrega apenas índices (`PackageMainIndex`, `Category`, `ProductMainIndex`,
`ItemIndex`, `CoinIndex`, `MileageFlag`). O preço usado é `product.Price`, resolvido do catálogo pelo
`CashShopSelectionResolver`. Confirmado no código.

## 5. Testes executados

| Comando | Resultado |
|---|---|
| `dotnet build src\MUnique.OpenMU.sln` | **0 Erro(s)** |
| `dotnet test tests\MUnique.OpenMU.Tests` | **602 aprovados, 0 falhas** |
| `dotnet test tests\MUnique.OpenMU.Persistence.Initialization.Tests` | **12 aprovados, 2 ignorados, 0 falhas** |
| `dotnet test tests\MUnique.OpenMU.Network.Packets.Tests` | **471 aprovados, 0 falhas** |

As provas contra o PostgreSQL desta sessão foram executadas por `dotnet ef` e por scripts `psql`, não
como fixture NUnit — ver seção 7, item 8.

## 6. Respostas de erro do protocolo

Valores confirmados no cliente (`ReceiveIGS_BuyItem`), preservados em `PurchaseErrorCode`:
0 sucesso, 1 saldo insuficiente, 2 sem espaço, 3 esgotado, 4 indisponível, 5 não selecionável,
6 não comprável, 7 item de evento, 8 limite de evento, 9 moeda errada, -1 erro de banco,
-2 falha de acesso.

`RollbackIncomplete = -100` é interno: **não** é enviado ao cliente, existe só para o servidor
distinguir falha limpa de contexto potencialmente sujo. O `CashShopViewPlugIn` converte para byte
two's complement (255/254) porque o cliente compara contra `static_cast<BYTE>(-1)` e `(-2)`.

## 7. Problemas ainda existentes

1. **Seed real não validado ponta a ponta.** Faltam os arquivos IBS e o `ConnectionConfigurator`
   impede apontar o inicializador para outro banco in-process. Precisa de um runner dedicado ou de um
   `Database=` configurável.
2. **Banco `openmu` está defasado** — 4 migrations de Cash Shop pendentes e catálogo vazio. Não apliquei
   nada nele, por decisão.
3. **Concorrência entre processos.** O `AsyncLock` por conta serializa apenas dentro de um processo.
   Dois processos ainda podem ler o mesmo saldo; o comentário em `WCoinService` admite isso. A
   constraint impede saldo negativo, não *lost update*.
4. **Mileage não implementado** — `D2 03` com `MileageFlag` é rejeitado com `CannotBeBought`.
5. **Gift bloqueado por `GiftFlag`** — o storage tem a coluna, mas presentes continuam bloqueados.
6. **Storage: falta exclusão/consumo** — o cliente não consegue retirar itens do storage.
7. **`WCoinC` / `GoblinPoints` não têm persistência** — só `WCoinWallet` existe.
8. **Fixture NUnit de PostgreSQL não entrou.** Tentei criar `CashShopPostgresValidationTests`, mas
   `EntityDataContext` não tem construtor com `DbContextOptions` e o `ConnectionConfigurator` é
   inicializado uma única vez por processo pelo `GlobalSetup`, apontando para `openmu`. Um fixture que
   criasse um contexto cairia no banco de desenvolvimento — risco que preferi **não** assumir. **O
   arquivo foi removido**; a validação ficou nos comandos documentados acima.

## 8. Decisões que dependem do proprietário

1. **Onde o seed deve rodar?** Um runner separado, ou tornar o `Database=` configurável por variável de
   ambiente (o provider já trata `DB_HOST` / `DB_ADMIN_USER` / `DB_ADMIN_PW`)?
2. **Vale aplicar as 4 migrations pendentes no banco `openmu`?** Precisa de backup confirmado antes.
3. **Os arquivos IBS reais estão disponíveis?** Sem eles o catálogo não é populado de verdade.
4. **Proteger contra `lost update` entre processos** (update condicional / advisory lock) ou aceitar o
   limite documentado?
5. **Prioridade do consumo do storage** — é o bloqueio mais visível para o jogador depois da compra.

## 9. Próximo passo recomendado

**Fechar o item 1 do diagnóstico antes de qualquer funcionalidade nova:** decidir o runner do seed e
obter os arquivos IBS. Só então popular o catálogo e fazer um teste manual de compra com o cliente
real, porque até lá a Fase 7 está correta em código e provada no banco, mas **nunca foi exercitada
ponta a ponta com o `Main.exe`**.

---

## Anexo — comandos de verificação reproduzíveis

```powershell
# migrations no banco isolado
cd src\Persistence\EntityFramework
dotnet ef database update --context EntityDataContext --connection "Server=localhost;Port=5432;User Id=postgres;Password=postgres;Database=openmu_cashshop_validation;Command Timeout=120;"

# inspeção do schema
psql -U postgres -h localhost -d openmu_cashshop_validation -c "select indexdef from pg_indexes where schemaname='data' and tablename='CashShopStorageItem';"

# as 4 migrations pendentes no banco de desenvolvimento (somente leitura)
psql -U postgres -h localhost -d openmu -c "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\" desc limit 6;"
```

**Nada foi escrito no banco `openmu`.** Todas as escritas ficaram em `openmu_cashshop_validation`,
que é descartável, e dentro de transações com `ROLLBACK` quando eram provas pontuais.
