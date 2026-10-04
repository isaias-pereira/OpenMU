# CashShop/XShop — Estado consolidado antes da Fase 7

> **Atualização pós-revisão da Fase 7:** este documento descreve o estado **antes** da Fase 7 e
> permanece como registro histórico. A Fase 7 foi implementada e revisada; o estado final,
> as evidências de protocolo, o escopo entregue e as pendências estão no relatório da tarefa
> (ver seção "Conclusão" ao final). Resumo: compra `C1 D2 03` com entrega no **CashShop Storage
> da conta** (entidade `CashShopStorageItem`), débito e entrega na **mesma** unidade de trabalho,
> listagem `D2 05 → D2 06 → D2 0D` implementada. Exclusão/consumo/gift do Storage continuam
> **pendentes**.

**Data do registro:** 2026-09-25  
**Escopo:** histórico e estado conhecido das Fases 0–6, mais o ponto de partida para a Fase 7.  
**Regra:** este documento descreve evidências encontradas e validações reportadas. Plano, scaffold, comentário ou arquivo existente não prova sozinho que um fluxo esteja funcional.

## Resumo executivo

A implementação tem uma base server-side para abrir a XShop, responder informações de pontos e armazenar/consultar um catálogo. A **Fase 6 está implementada em código e possui testes/build reportados como aprovados**, mas ainda não está validada operacionalmente com a migration nova e o seed executados em PostgreSQL de desenvolvimento.

A **Fase 7 ainda não está funcionalmente integrada**. Existem structs de pacote, um writer da resposta D2 03 e scaffolds de action/serviço de compra, mas não foi localizado handler de entrada D2 03, registro DI do serviço/action nem entrega real do item. O scaffold atual pode debitar WCoin antes de uma entrega funcional; não deve ser tratado como uma compra pronta.

**Estado recomendado na transição:** preservar e revisar as alterações locais da Fase 6; validar migration e importação num PostgreSQL isolado; esclarecer a semântica do catálogo e do pacote D2 03; então começar a integração da Fase 7 sem reutilizar as APIs ambíguas por sequência simples.

## Baseline do repositório

- Repositório: `C:\Dev\MuOpenSource\OpenMU`
- Branch: `cashshop`
- HEAD observado: `265b22009a8745dd93ec7f7cc5404543be3564db` — `feat: consolidate Cash Shop integration and deployment updates`
- Último estado observado antes de criar este documento: **14 arquivos rastreados modificados, 5 arquivos não rastreados e nenhum commit novo**. Essas mudanças incluem a implementação da Fase 6 e a correção do fluxo de inicialização do catálogo. Este documento acrescenta mais um arquivo novo.
- Não usar `reset`, `clean` ou outra operação destrutiva para começar a Fase 7. Revisar e preservar o diff local primeiro.
- `AGENTS.md` e `CLAUDE.md` do repositório contêm as instruções de implementação. Os documentos externos em `C:\Users\isaia\OneDrive\Desktop\Cashsho` são referências de planejamento/status e podem estar desatualizados.

## Histórico por fase

Classificações: **CONFIRMADO** = evidência no código atual ou artefato rastreado; **PARCIAL** = parte implementada, integração/validação incompleta; **DESCONHECIDO** = depende de cliente, banco ou evidência que não foi validada.

| Fase | Objetivo | Estado antes da Fase 7 |
|---|---|---|
| 0 — Baseline | Registrar branch, commit, arquitetura, build e testes iniciais. | **PARCIAL.** Branch/HEAD e arquitetura atual foram inspecionados. Não há baseline histórico completo de build/testes antes de CashShop que possa ser confirmado apenas pelo repositório. |
| 1 — Comparação com `94592a6` | Comparar a implementação OpenMU com o commit de referência. | **DESCONHECIDO / sem relatório verificável no checkout.** Não assumir que houve comparação arquivo a arquivo. |
| 2 — Mapeamento D2 | Mapear grupo, subpacotes e formatos. | **PARCIAL.** Existem definições C2S D2 01, 02, 03, 04, 05, 0A, 0B e 13 em `src/Network/Packets/ClientToServer/ClientToServerPackets.cs`, mais documentos dos requests em `docs/Packets/`. Os handlers só cobrem alguns subpacotes. Formatos descritos pelo cliente não substituem uma validação de ponta a ponta com `Main.exe`/trace. |
| 3 — Abertura da XShop | Implementar a resposta de abertura e a condição de acesso. | **PARCIAL.** `CashShopOpenStateAction` aplica a regra de SafeZone; o plugin envia versões de script/banner ao entrar no mundo. A abertura real no cliente instalado não foi validada nesta etapa consolidada. |
| 4 — Point Info / moedas | Responder pontos e conectar moedas. | **PARCIAL**, apesar de status histórico declarar a fase concluída. A resposta D2 01 e a wallet WCoinP existem. `CashShopPointInfoAction` envia zero para WCoinC, pontos e milhagem; GoblinPoints não foi localizado como saldo persistido/serviço CashShop. |
| 5 — Script/Banner | Fazer o cliente carregar catálogo e banners. | **PARCIAL.** O servidor envia versões D2 0C e 15; não foi localizado envio de conteúdo de Script/Banner. A documentação indica catálogo local instalado no cliente, mas esse contrato precisa ser confirmado para a versão de `Main.exe` usada. |
| 6 — Catálogo | Modelar, persistir, importar e consultar Category/Package/Product. | **IMPLEMENTADA EM CÓDIGO E TESTES REPORTADOS; validação operacional PostgreSQL pendente.** Detalhes abaixo. |
| 7 — Compra D2 03 | Receber request, validar compra, cobrar e concluir operação segura. | **AINDA NÃO INTEGRADA.** Há scaffolds, mas não um fluxo funcional completo. É a próxima fase, sujeita às pendências abaixo. |
| 8 — Gift D2 04 | Enviar compra como presente. | Tipos C2S existem; handler, ação, persistência/armazenamento de presente e resposta não foram localizados como fluxo integrado. |
| 9 — Storage | Listar, excluir e consumir itens do armazenamento CashShop. | D2 05 responde uma lista vazia/contagem vazia. D2 0A e 0B têm tipos C2S, mas não foi localizado fluxo completo nem persistência CashShop de storage. |
| 10 — Event Shop | Listar itens/eventos. | D2 13 e resposta de quantidade existem. O pacote de lista D2 14 e a semântica completa do catálogo de evento não foram localizados. |
| 11 — Segurança | Garantir validação e atomicidade nos fluxos comerciais. | **PENDENTE para compra/gift/storage.** O débito WCoin já existente não basta para provar atomicidade com entrega, idempotência ou validação completa do pacote. |
| 12 — Site/API/WCoin | Integrar o CashShop ao site/API. | WCoin e alguns fluxos VIP existem; API específica de CashShop não foi localizada. |
| 13 — Produção | Validar deploy e fluxo real em produção. | **PENDENTE.** Compose/PostgreSQL existem, mas o fluxo completo de cliente, catálogo e compra não foi validado em produção. |

## Fase 4: implementação confirmada e limites

O histórico Git registra o commit `36c960e22` como implementação da Fase 4. O código atual contém:

- grupo de mensagens `0xD2` em `src/GameServer/MessageHandler/CashShop/CashShopGroupHandlerPlugIn.cs`;
- handlers de Point Info (01), Open State (02), Storage List (05) e Event Item List (13);
- resposta D2 01 ligada a `WCoinService` para WCoinP;
- respostas manuais em `src/Network/Packets/ServerToClient/CashShopConnectionExtensions.cs`, incluindo Point Info, Open Result, Storage Count, Script Version, Event Item Count, Buy Result e Banner Version;
- push de versões 0C/15 e Point Info em `src/GameLogic/PlugIns/CashShop/CashShopVersionPlugIn.cs`;
- resposta de storage 05/06 atualmente vazia.

O arquivo `docs/Progress.md` ainda lista CashShopGroup como `0xF5`, enquanto os handlers e pacotes CashShop do código usam `0xD2`. Tratar essa linha do progresso como desatualizada; corrigir a documentação geral em uma tarefa própria, sem confundir com evidência de protocolo.

## Fase 5: contrato Script/Banner

O servidor envia os números de versão, mas não há implementação de transferência dos arquivos IBS nem conteúdo de banners localizada. Os comentários do plugin e o plano descrevem que o cliente pode carregar o catálogo localmente. Portanto:

- **CONFIRMADO no servidor:** envio de versões D2 0C e 15;
- **NÃO CONFIRMADO:** o cliente específico abre e carrega a loja apenas com esses pacotes e os arquivos locais;
- **FORA DO ESCOPO DA Fase 6:** inventar ou adicionar payloads Script/Banner sem evidência do cliente.

## Fase 6: estado técnico

### Modelo e persistência

Entidades do catálogo:

- `src/DataModel/Entities/CashShopCategory.cs`
- `src/DataModel/Entities/CashShopPackage.cs`
- `src/DataModel/Entities/CashShopProduct.cs`
- `src/DataModel/Entities/CashShopPackageProduct.cs`

As entidades estão ligadas a `GameConfiguration` e aos modelos EF/BasicModel. O mapeamento é mantido em `src/Persistence/EntityFramework/Extensions/ModelBuilder/CashShopModelBuilderExtensions.cs` e o snapshot está em `src/Persistence/EntityFramework/Migrations/EntityDataContextModelSnapshot.cs`.

Migrations relevantes:

- `20260919172300_AddCashShopCatalog`
- `20260923062714_GrantCashShopConfigRole`
- `20260925085448_FixCashShopCatalogNaturalKeys`

A migration mais nova ajusta as chaves naturais para refletir as repetições observadas no IBS: produto `(ProductSeq, PriceSeq, PropertySeq)` e pacote `(ProductDisplaySeq, PackageProductSeq)`. **Essa migration não foi aplicada ao PostgreSQL no relatório de validação.**

### Parser, seed e startup

- Parser: `src/Persistence/Initialization/DataInitialization/CashShopCatalogParser.cs`.
- Inicializador: `src/Persistence/Initialization/DataInitialization/CashShopCatalogInitializer.cs`.
- Startup: `src/Persistence/Initialization/DataInitialization/CashShopCatalogStartupInitializer.cs`.
- Variável opcional de origem dos arquivos: `OPENMU_CASH_SHOP_CATALOG_PATH`.
- Startup também é coordenado em `src/Startup/Program.cs`; a atualização automática e a confirmação manual usam o mesmo fluxo, e o seed acontece depois da atualização bem-sucedida. A cobertura está em `tests/MUnique.OpenMU.Tests/CashShop/CashShopCatalogStartupFlowTests.cs`.

Comportamento observado/reportado:

- parser exige 7/27/17 campos em Category/Package/Product, usa `@` e listas `|`, tenta UTF-8 e cai para CP949;
- erro de parse interrompe a importação, em vez de ignorar silenciosamente o registro inválido;
- seed valida chaves/referências e adiciona somente entidades/relacionamentos ausentes;
- valores existentes divergentes são preservados com warning; registros que sumiram do IBS não são removidos. Logo, o seed é aditivo/idempotente para reexecução, mas **não sincroniza nem atualiza** o banco;
- variável de ambiente ausente faz o seed ser ignorado;
- o serviço `ICashShopCatalogService`/`CashShopCatalogService` lê por `GameConfiguration`, mantém cache segmentado por configuração e filtra por `DeleteFlag == 181` e datas.

O catálogo IBS real foi reportado no caminho local `C:\Users\isaia\MuMain\src\bin\Data\InGameShopScript\512.2012.076`, fora do repositório. Contagens reportadas: 22 categorias, 210 pacotes e 2.603 produtos. Package/Product produz 310 tuplas no parser e 962 junções depois de expandir variantes de produto. O parser implementa produto cartesiano entre `ProductSeqList` e `PriceSeqList`, e o seed liga todas as variantes que correspondem a `ProductSeq`/`PriceSeq`. **Antes de usar essas relações para cobrar/entregar itens na Fase 7, validar que essa interpretação corresponde ao contrato IBS/cliente.**

O relatório informa que as datas dos pacotes nesse catálogo são antigas (2010–2015). Com o filtro de vigência atual, isso pode resultar em zero pacotes ativos em 2026. Não mudar datas nem flags artificialmente; obter um catálogo atual ou uma regra de negócio confirmada.

### Validação reportada

O relatório da IA que executou a Fase 6 informa:

- build do projeto Persistence Initialization sem erros;
- build Startup sem erros (com avisos reportados);
- suíte principal: 493 aprovados, 0 falhas;
- suíte Persistence Initialization: 7 aprovados, 2 ignorados, 0 falhas;
- testes de parser/serviço/inicializador/startup incluídos;
- script idempotente de migration gerado e ausência de pending model changes;
- probe com IBS real executado fora do repositório.

Esses números são **resultados reportados pela IA anterior**, não foram reexecutados durante a criação deste documento. Não houve aplicação da migration nova nem seed real em PostgreSQL. O banco disponível não foi identificado como ambiente descartável inequívoco, então nenhuma escrita foi feita.

## Ponto de partida para a Fase 7

### O que já existe como scaffold

- request C2S D2 03 `CashShopItemBuyRequest` em `src/Network/Packets/ClientToServer/ClientToServerPackets.cs`;
- documentação do request em `docs/Packets/C1-D2-03-CashShopItemBuyRequest_by-client.md`;
- resposta S2C de resultado D2 03 em `CashShopConnectionExtensions.SendCashShopBuyResultAsync`;
- `CashShopBuyAction`, `ICashShopPurchaseService` e `CashShopPurchaseService`.

### O que ainda falta para compra funcional

- não foi localizado handler de entrada D2 03 em `src/GameServer/MessageHandler/CashShop/`;
- `ICashShopPurchaseService` e `CashShopBuyAction` não estão registrados no DI como fluxo de compra;
- o request contém índices/campos do cliente; o mapeamento desses campos para as chaves compostas do catálogo ainda precisa ser provado;
- a implementação de serviço atual procura por sequências simples em overloads legados, apesar de o catálogo real repetir `PackageProductSeq` e `PriceSeq`;
- o serviço faz débito WCoin, mas a entrega do item é placeholder; não há operação atômica compra + entrega/armazenamento;
- o código aceita coin types diferentes e os encaminha à mesma wallet WCoin; mileage também não tem semântica implementada;
- não foram localizados testes específicos do fluxo D2 03/compra.

**Regra de segurança para a Fase 7:** não conectar um handler que possa debitar saldo até que validação do request, pacote/produto/preço/moeda e entrega tenham contrato definido e sejam testados em conjunto. Não usar sequência simples onde os dados confirmam chave composta. Confirmar a relação com o cliente real e não afirmar suporte a WCoinC/GoblinPoints/mileage sem persistência e regra comprovadas.

## Próximos passos recomendados, ainda antes de ativar compras

1. Preservar/revisar as alterações locais da Fase 6 e confirmar o diff antes de iniciar edição da Fase 7.
2. Preparar PostgreSQL isolado de desenvolvimento; conferir a origem do banco e backup/cópia antes de escrita.
3. Aplicar a migration `20260925085448_FixCashShopCatalogNaturalKeys` somente nesse ambiente e executar o seed com os três arquivos IBS reais.
4. Conferir contagens, relações, reexecução, cache e resultado de pacotes vigentes. Registrar zero pacotes ativos como efeito das datas antigas, se ocorrer.
5. Antes da compra, estudar os campos do D2 03 em `docs/Packets/` e sua correspondência no cliente; definir a fronteira entre compra, entrega e storage. Não usar a implementação placeholder para cobrar jogadores.

## Fontes e limites deste registro

- Código e histórico Git do checkout atual; baseline observado no início desta documentação.
- `AGENTS.md`, `CLAUDE.md`, `docs/Progress.md` e documentos locais em `docs/Packets/`.
- Planos/status externos em `C:\Users\isaia\OneDrive\Desktop\Cashsho` e relatórios de execução fornecidos pelo proprietário. Esses arquivos não são versionados neste repositório e podem ficar desatualizados.
- Não foram executados build, testes, migration, seed ou acesso ao banco durante a criação deste documento.
