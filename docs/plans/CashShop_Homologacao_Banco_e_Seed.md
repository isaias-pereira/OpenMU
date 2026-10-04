# CashShop/XShop — Homologação: banco, seed e correções

**Data:** 2026-09-29
**Branch:** `cashshop` · **HEAD:** `56cf0725e`
**Escopo:** preparar o banco de homologação, popular o catálogo e corrigir os defeitos que impediam a
repetibilidade.

> Documento de registro verificado. `CashShop_Status_Pre_Fase_7.md` (histórico) e
> `CashShop_Fase_6_e_7_Validacao_PostgreSQL.md` (validação anterior) permanecem intactos.

## 1. Estado inicial

- 72 alterações locais, todas preservadas. Nenhum `reset`/`clean`.
- PostgreSQL 15.18 local. Docker instalado com **daemon parado** — não usado.
- `openmu` (desenvolvimento) com 50 migrations e catálogo vazio. **Não foi alterado.**

## 2. Correções realizadas

### 2.1 `DB_NAME` — banco configurável (item 7 da ETAPA 2)

`ConfigFileDatabaseConnectionStringProvider` aplicava `DB_HOST`, `DB_ADMIN_USER` e `DB_ADMIN_PW`, mas
**não tinha como trocar o nome do banco**: o `Database=` estava preso ao `ConnectionSettings.xml`.
Dois defeitos encontrados e corrigidos:

1. **`DB_NAME` inexistente.** Adicionado. A troca é por chave, então funciona com qualquer valor
   configurado.
2. **`DB_ADMIN_PW` silenciosamente ignorado.** O código substituía a string literal
   `Password=admin;`, mas o arquivo distribuído usa `Password=postgres;`. A substituição nunca
   casava e a variável era descartada sem aviso. Agora a troca é por chave.

Cobertura: `ConfigFileDatabaseConnectionStringProviderTests` (5 casos, incluindo variável vazia e
preservação da senha de papéis não-admin).

### 2.2 Compose com variável que o código realmente lê

`deploy/all-in-one/docker-compose.yml` definia `ConnectionStrings__DefaultConnection`, que **nenhum
código lê** — as strings vêm do `ConnectionSettings.xml`. Substituído por `DB_NAME: openmu`, com
comentário explicando o porquê.

### 2.3 Seed do catálogo não era idempotente (defeito real)

A segunda execução do seed falhava com:

```
The instance of entity type 'CashShopPackageProduct' cannot be tracked because another instance
with the same key value for {'PackageId', 'ProductId'} is already being tracked.
```

**Causa raiz:** `GetAsync<CashShopPackageProduct>` devolvia as linhas com `PackageId` e `ProductId`
iguais a `Guid.Empty`. No modelo EF, `CashShopPackageProduct` declara `PackageId`/`ProductId` como
`Guid?` que **ocultam** as propriedades não-nulas do tipo base, e o repositório projeta a linha sem
preenchê-las. A deduplicação comparava chaves vazias, nunca casava, e a segunda execução tentava
inserir uma relação já presente.

**Correção:** a deduplicação passa a ler as **navegações** (`relation.Package.Id`,
`relation.Product.Id`), que carregam os identificadores reais.

**Antes:** a 2ª execução lançava exceção. **Depois:** conclui com `Catalog seed completed.` e as
contagens permanecem `2 categorias, 1 pacote, 4 produtos, 4 relações`, com `0` chaves duplicadas.

## 3. Banco de homologação

Recriado do zero, sem tocar em `openmu`:

```powershell
# 1. banco
psql -U postgres -h localhost -c 'drop database if exists openmu_cashshop_validation;'
psql -U postgres -h localhost -c 'create database openmu_cashshop_validation;'

# 2. migrations — sem --connection, usando só a variável de ambiente
cd src\Persistence\EntityFramework
$env:DB_NAME='openmu_cashshop_validation'
dotnet ef database update --context EntityDataContext

# 3. seed + conta de teste
cd ..\..\..
dotnet run --project tools\cashshop-homologation
```

**Prova de que `DB_NAME` funciona:** as migrations foram aplicadas **sem** `--connection`, apenas com
a variável. Resultado: `Done.`, 56 migrations, 5 tabelas de Cash Shop. `openmu` continuou com 50.

## 4. Estado final do catálogo — VERIFICADO (2026-09-30)

> Esta seção substitui a contagem sintética registrada anteriormente. Os números abaixo foram lidos
> do PostgreSQL depois de duas execuções do runner.

Catálogo **real** IBS, semeado a partir de
`C:\Dev\MU Client 1.04d - Season 6E3\Data\InGameShopScript\512.2012.084`
via `OPENMU_CASH_SHOP_CATALOG_PATH` — e não do catálogo sintético:

```
Parsed 22 categories, 212 packages, 2720 products, 310 relations.
```

Contagem no banco `openmu_cashshop_validation` após duas execuções (idempotência comprovada — a
segunda execução não duplicou linhas):

| Entidade | Linhas |
|---|---|
| ItemDefinition | 676 |
| CharacterClass | 18 |
| GameMapDefinition | 73 |
| CashShopCategory | 22 |
| CashShopPackage | 213 |
| CashShopProduct | 2722 |
| CashShopPackageProduct | 966 |
| Account | 1 |
| Character | 1 |
| WCoinWallet | 1 |

Personagem de homologação criado: `Cshomolo`, classe `Soul Master`. Carteira `cshomolo` = 100000.
`Account.LoginName` é `varchar(10)`, o que restringiu o login a `cshomolo`.

**Ressalva sobre os totais:** o banco tem 213 pacotes e 966 relações, acima dos 212/310 do último
parse, porque execuções anteriores deixaram linhas do catálogo sintético. O runner atualiza por
chave, mas **não** apaga linhas que sumiram do catálogo de origem. Para começar do zero é preciso
`drop database` e recriar.

`tools/cashshop-homologation/catalog/` continua sendo o catálogo **sintético** de exemplo, usado só
quando `OPENMU_CASH_SHOP_CATALOG_PATH` não está definida.

## 4.1 Estado anterior do catálogo (catálogo sintético, substituído)

```
 cat | pkg | prod | rel | storage
-----+-----+------+-----+--------
   2 |    1 |    4 |   4 |       0

 PackageProductSeq | CashType | Order | ProductSeq | PriceSeq | Price | DeleteFlag
-------------------+----------+-------+------------+----------+-------+------------
               100 |      509 |     0 |        528 |      607 |   500 |        144
               100 |      509 |     1 |        528 |      608 |   400 |        144
               100 |      509 |     2 |        529 |      607 |  1200 |        144
               100 |      509 |     3 |        529 |      608 |   900 |        144

 LoginName | Balance
-----------+----------
 cshomolo  |  100000
```

Preços e flag de disponibilidade vêm do servidor. `Account.LoginName` é `varchar(10)`, o que
restringiu o login a `cshomolo`.

Os arquivos IBS ficam em `tools/cashshop-homologation/catalog/`. São **dados de homologação
sintéticos**, criados para este ambiente — não são o catálogo real de IBS.

## 5. Ferramenta de seed

`tools/cashshop-homologation` — executa o seed em processo separado, que é o que torna um banco
isolado alcançável: o servidor semeia só na inicialização e sempre contra o `ConnectionSettings.xml`
de onde roda. O runner também cria a conta e a carteira, porque sem `WCoinWallet` o débito responde
saldo insuficiente e a compra não é exercitável.

## 6. Testes

| Comando | Resultado |
|---|---|
| `dotnet build src\MUnique.OpenMU.sln` | **0 Erro(s)** |
| `dotnet test` (MUnique.OpenMU.Tests) | **602 aprovados, 0 falhas** |
| `dotnet test` (Persistence.Initialization.Tests) | **21 aprovados, 2 ignorados, 0 falhas** |
| `dotnet test --filter FullyQualifiedName~CashShop` | **90 aprovados, 0 falhas** |

## 6.0 Limpeza controlada e conferência catálogo x origem (2026-09-30)

Banco `openmu_cashshop_validation` derrubado e recriado do zero, **56 migrations**, inicialização
Season 6 e seed apenas dos IBS reais. `openmu` não foi tocado. Resultado final:

| Entidade | Origem IBS | Banco | Estado |
|---|---|---|---|
| CashShopCategory | 22 | 22 | idêntico |
| CashShopPackage | 212 | 212 | idêntico |
| CashShopProduct | 2720 | 2720 | idêntico |
| CashShopPackageProduct | 962 | 962 | idêntico |

Comparação registro a registro (chave `ProductDisplaySeq|PackageProductSeq` para pacotes,
`ProductSeq|PriceSeq|PropertySeq` para produtos e junctions): **zero divergências**, nenhum registro
existente no banco que não exista nos IBS, e nenhum registro dos IBS que não tenha sido inserido.
Nomes, preços, `DeleteFlag`, `StorageGroup`, `MustFlag` e `ShareFlag` conferem nos 2720 produtos.
`DeleteFlag`, `EventFlag`, `SalesFlag` e `GiftFlag` conferem nos 212 pacotes.

Única diferença esperada: as **datas**. `OPENMU_CASH_SHOP_REOPEN_PACKAGES=1` desloca a vigência de
todos os pacotes para `20260929051727 → 20310930051727`, porque os IBS descrevem a campanha de
2011–2016. Isso afeta só este ambiente de homologação; o filtro de produção não muda.

### 6.0.1 Investigação da discrepância 310 / 962 / 966

**`310` é o número de registros de relação, não de linhas da tabela.** `CashShopCatalogParser`
gera, para cada pacote, o **produto cartesiano** de `ProductSeqList` × `PriceSeqList`. São 212
pacotes × as listas → **310 registros de relação**.

Ao persistir, `CashShopCatalogInitializer` resolve cada registro procurando produtos por
`(ProductSeq, PriceSeq)`. Em `IBSProduct.txt` **872 dos 874 pares** `(ProductSeq, PriceSeq)`
aparecem com **vários `PropertySeq`** (o mesmo produto em variantes 7, 35, 51…). Cada variante é uma
linha distinta em `CashShopProduct`, então **um registro de relação vira várias junctions**:
**310 → 962**. Simulei a resolução completa e obtive exatamente 962, sem duplicatas.

`966` era o número do **banco sujo**: 962 reais + 4 do catálogo sintético de exemplo. Não há
defeito de contagem — os três números medem coisas diferentes.

Confirmado por inspeção: a chave primária é `(PackageId, ProductId)`, a deduplicação em
`InitializePackageProductRelationsAsync` usa o par de navegação (não os `Guid?` que ocultam as
propriedades não-nulas) e uma simulação Offline dos 962 junctions **não produz nenhuma colisão**.

### 6.0.2 Defeito real encontrado: o seed roda duas vezes na 1ª execução

Em banco vazio, a **primeira** execução falha com
`23505 duplicate key ... PK_CashShopPackageProduct`.

Causa: `DataInitializationBase.CreateInitialDataAsync` chama `InitializeCashShopCatalogAsync` na
linha 180, **antes** do `SaveChangesAsync` da linha 182. Como `OPENMU_CASH_SHOP_CATALOG_PATH` está
definida, `CashShopCatalogStartupInitializer` semeia o catálogo **dentro** da inicialização oficial.
O runner chama `CashShopCatalogInitializer` de novo logo depois (linha 124), e a segunda passagem
tenta reinserir junctions já rastreadas pelo mesmo `GameConfiguration` — colisão de chave.

A **segunda** execução passa, porque `EnsureGameDataAsync` detecta os `ItemDefinition` já
presentes, pula a inicialização oficial e semeia uma única vez. As execuções 2 e 3 foram limpas
(sem erro, sem duplicatas), mas a 1ª execução em banco novo **não é confiável**.

**Resolvido** — o parser **não** foi alterado. A causa era de orquestração, e a correção foi em
`src/Persistence/Initialization/DataInitializationBase.cs` e `tools/cashshop-homologation/Program.cs`.

**Causa real (mais precisa que a hipótese inicial).** Não era o runner chamando o seed duas vezes.
O seed usava `this.GameConfiguration` — a instância **ainda não persistida**, rastreada por
`this.Context`. Como o seed acrescenta categorias, pacotes, produtos e junctions às coleções de
navegação dessa mesma instância, `this.Context` passava a rastrear o catálogo inteiro. O
`SaveChangesAsync` da linha 182, chamado **depois** do seed, tentava inserir linhas que o seed já
tinha gravado → `PK_CashShopPackageProduct`.

**Correção — um único responsável, na ordem desejada:**

1. `DataInitializationBase`: salvar os dados-base **antes** do seed. O `SaveChangesAsync` da linha
   182 passou a vir antes de `InitializeCashShopCatalogAsync`, então a configuração já está
   persistida e libera a unit of work; o seed abre seu próprio contexto e grava o catálogo por conta
   própria. Nenhuma linha é inserida duas vezes.
2. Runner: passar a ser apenas o dono explícito do seed, com comentário explicando que
   `CreateInitialDataAsync` já o executa e que a segunda chamada é inofensiva por ser idempotente.

Isso preserva a produção: `Startup.Program` continua chamando `InitializeIfConfiguredAsync` nos
três caminhos, e `OPENMU_CASH_SHOP_CATALOG_PATH` continua sendo a única chave de configuração.

### 6.0.3 Validação limpa após a correção (2026-09-30)

`openmu_cashshop_validation` destruída e recriada do zero (3 sessões abertas foram encerradas antes),
56 migrations, e o processo executado **duas vezes**:

| | 1ª execução | 2ª execução |
|---|---|---|
| `PK_CashShopPackageProduct` | **não ocorreu** | **não ocorreu** |
| Season 6 | executou | pulou (dados presentes) |
| seed | 1 vez | idempotente |
| conta / personagem / carteira | criados | reutilizados |

Contagens finais confirmadas no PostgreSQL:

| Entidade | Esperado | Banco |
|---|---|---|
| CashShopCategory | 22 | 22 |
| CashShopPackage | 212 | 212 |
| CashShopProduct | 2720 | 2720 |
| CashShopPackageProduct | 962 | 962 |
| ItemDefinition | 676 | 676 |
| CharacterClass | 18 | 18 |
| GameMapDefinition | 73 | 73 |
| Account | 1 | 1 |
| Character | 1 | 1 |
| WCoinWallet | 1 | 1 |
| CashShopStorageItem (sintético) | 0 | 0 |
| Pacotes vencidos | 0 | 0 |

Conferência contra os IBS reais: **chaves 212/212 idênticas**, **relações 962/962 idênticas**,
**produtos 2720/2720 idênticos** em `ProductSeq`, `PriceSeq`, `PropertySeq`, preço, `DeleteFlag` e
`StorageGroup`. Nos nomes, 203 são idênticos e 9 diferem apenas por **espaço final** removido na
persistência (ex.: `Gladiator Package ` → `Gladiator Package`). **Nenhuma divergência real**, nenhum
registro sintético.

Suítes: build **0 erros**, `MUnique.OpenMU.Tests` **602/602**, `Persistence.Initialization.Tests`
**21 aprovados / 2 ignorados**, filtro `CashShop` **90/90**.


### 6.1 `LocalizedString` x PostgreSQL — o bloqueio da inicialização (resolvido)

A inicialização oficial Season 6 falhava contra PostgreSQL limpo com:

```
System.Text.Json.JsonException: The JSON value could not be converted to
MUnique.OpenMU.Interfaces.LocalizedString
```

**Causa raiz.** `JsonQueryBuilder` monta a leitura com `row_to_json`, que serializa cada coluna pelo
tipo nativo do PostgreSQL. `LocalizedString` mora numa coluna `varchar`, então chega como string JSON
simples (`"Name": "Dark Wizard"`). Como `LocalizedString` é um `struct` de propriedade única, o
`System.Text.Json` tentava ler objeto e falhava. O lado da escrita tinha o problema espelhado:
`JsonObjectSerializer` emitia `{"Value": "...", "ValueInNeutralLanguage": "..."}`.

O projeto **já tinha** o `LocalizedStringJsonConverter` correto — ele só era registrado em
`Startup.Program` e `DaprService`, nunca na camada de persistência. Qualquer host que usasse o
caminho EF/PostgreSQL sem passar por esses pontos de entrada (testes, o runner de homologação) não o
tinha. O mesmo valia para o `BinaryAsHexJsonConverter`, cujo motivo o próprio arquivo já documentava
e que apareceu em seguida como `The JSON value could not be converted to System.Byte[]`.

**Correção** — registrar os dois conversores na camada de persistência, de forma idempotente, para
que nenhum host dependa dos pontos de entrada da aplicação:

- `src/Persistence/EntityFramework/Json/JsonObjectDeserializer.cs` — registra
  `LocalizedStringJsonConverter` e `BinaryAsHexJsonConverter` se ainda não presentes.
- `src/Persistence/Json/JsonObjectSerializer.cs` — registra `LocalizedStringJsonConverter` na
  escrita, para leitura e escrita usarem o mesmo formato.

`InMemoryPersistenceContextProvider` não é afetado: mantém objetos vivos, sem passar por JSON.
Nenhuma migration foi necessária — o formato gravado (string) é o mesmo que o conversor lê.

**Regressão:** `tests/MUnique.OpenMU.Persistence.Initialization.Tests/LocalizedStringPostgresJsonTests.cs`
(4 casos: formato `row_to_json`, coluna `null`, formato da escrita e o conversor isolado).
Falhou 3/4 antes da correção e passa 4/4 depois.

### 6.2 `CharacterClass` detachada na criação do personagem

A criação do personagem falhava com `duplicate key value violates unique constraint
PK_DropItemGroupItemDefinition`. O `CharacterClass` vem de um repositório apoiado em contexto
`NoTracking`, então está solta; ao atribuí-la a um personagem novo, o EF tratava todo o grafo
alcançável (incluindo os drop item groups da classe) como novo e tentava reinseri-lo. O runner
agora chama `IContext.Attach` antes de associar, marcando o grafo como já existente.

## 7. O que ainda falta

**A Cash Shop NÃO está pronta.** A base de dados está validada; falta o teste ponta a ponta com o
cliente. Os itens 2 e 3 abaixo foram resolvidos e estão riscados na lista histórica.

1. **A compra não foi validada com o `Main.exe`.** O servidor não foi iniciado: exige o pipeline
   completo de login e um cliente. Banco pronto, catálogo conferido com os IBS reais, conta com
   saldo e personagem criado — mas a compra real continua não exercitada.
2. ~~A conta `cshomolo` não tem personagem.~~ **Resolvido**: `Cshomolo` (Soul Master) existe.
3. ~~Faltam os arquivos IBS reais.~~ **Resolvido**: usados `512.2012.084`, 212 pacotes / 2720 produtos.
4. **Consumo do storage** continua não implementado — o jogador compra mas não consegue retirar.
5. **Mileage** rejeitado; **gift** bloqueado por `GiftFlag`.
6. **`lost update` entre processos** permanece: a constraint impede saldo negativo, não leitura perdida.

### 7.1 Checklist pendente para a conclusão

A conclusão só é válida depois de todos estes pontos passarem com o cliente real:

- [ ] login
- [ ] seleção do personagem
- [ ] abertura da Cash Shop
- [ ] carregamento do catálogo
- [ ] compra real
- [ ] débito do WCoin
- [ ] entrega do item
- [ ] persistência após reiniciar o servidor

### 7.2 Como iniciar o servidor contra o banco de homologação

O `ConnectionSettings.xml` continua apontando para `openmu` e **não deve ser editado** — é arquivo
versionado. `ConfigFileDatabaseConnectionStringProvider` lê `DB_NAME`, então o destino é escolhido
pelo ambiente:

```powershell
$env:DB_NAME                        = 'openmu_cashshop_validation'
$env:OPENMU_CASH_SHOP_CATALOG_PATH   = 'C:\Dev\MU Client 1.04d - Season 6E3\Data\InGameShopScript\512.2012.084'
$env:OPENMU_CASH_SHOP_REOPEN_PACKAGES = '1'   # exclusivo da homologação
dotnet run --project src\Startup
```

Credenciais: `cshomolo` / `homologation`, saldo 100000 WCoin, personagem `Cshomolo`.
As roles `config`, `account`, `friend` e `guild` já existem e alcançam o banco de homologação
(verificado com um `select count(*)` usando a role `config`).

## 8. Decisões que dependem do proprietário

1. Os **arquivos IBS reais** substituem os sintéticos?
2. Criar conta e classes de homologação pelo seeder ou por script dedicado?
3. Devo **iniciar o servidor** neste ambiente para a validação com cliente, ou isso fica do seu lado?

## 9. Próximo passo

Iniciar o servidor apontado para `openmu_cashshop_validation` e executar a compra `D2 03` com o
cliente. O bloqueio deixa de ser dados e passa a ser o cliente real.

---

## Anexo — ambiente

```powershell
$env:DB_NAME='openmu_cashshop_validation'   # banco alvo
$env:DB_HOST='localhost'                    # opcional
$env:DB_ADMIN_USER='postgres'               # opcional
$env:DB_ADMIN_PW='postgres'                 # opcional
```

**Nada foi escrito em `openmu`.** Todas as escritas ficaram em `openmu_cashshop_validation`.

