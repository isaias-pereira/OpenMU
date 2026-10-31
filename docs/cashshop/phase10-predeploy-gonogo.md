# Fase 10 — Pré-Deploy RC1: Verificação GO/NO-GO e Execução Preparada

Data: 2026-10-24 · **DECISÃO: GO — JANELA DE HOMOLOGAÇÃO AUTORIZÁVEL** (nenhuma divergência de schema não compreendida; nenhum procedimento destrutivo executado nesta rodada).

---

## 1. Identidade da release

| Item | Valor |
|---|---|
| Imagem candidata | `munique/openmu:cashshop-phase10-rc1` |
| Image ID (completo) | `sha256:2512867e09fd605613d3421bd9fe3e3d22840fa739d64225e7eeedebde96539e` |
| Criada em | 2026-10-24T23:10:30Z (build exit 0) |
| Dockerfile | `Dockerfile - Copia.openmu` |
| Build context | `C:\Dev\MuOpenSource\OpenMU-cashshop-9b0b68292` (worktree integrado) |
| Branch/HEAD | `checkpoint/cashshop-phase10-isolated` @ `9b0b68292` + **60 alterações pendentes** (não commitadas, por autorização) |
| Imagem atual do container `openmu` | `sha256:ab57829c5abe569a53b8c9c5fa405f908f817741cf98cc4c5c44295d49f1820f` = `munique/openmu:custommove-fix-20261018` (Up; **não é rollback funcional pós-itens temporários**) |

Presenças na RC1 (verificadas no código fonte + testes): expiração real (`ItemExpiryPolicy`, `ExpiredTemporaryItemService`, `ActivatedAtUtc`/`ExpiresAtUtc`/`DurationSeconds`), Claim otimizado (fallback 2AT + ativação única), warmup do catálogo (`WarmupAsync` no boot), CustomMove v2 (`/custommove` desacoplado + validações de mapa/coordenada + banner `PluginActive`), enforcement central (equip/use/trade/craft/atributos/stack).

Manifesto SHA256 (16 primeiros hex) dos 22 arquivos críticos: `C:\Windows\Temp\rc1-manifest.txt` (ex.: migration `EABBD0CEB96EA826`, policy `1D819F4FC381A9C1`, sweep `85017641F3B0BDF1`).

Commits locais sugeridos por escopo (NÃO executar sem autorização): (1) `feat(cashshop): item expiry persistence` — DataModel + migration + snapshot; (2) `feat(cashshop): claim activation + duration freeze` — buy/gift/claim; (3) `feat(cashshop): expiry enforcement + cleanup` — policy/service/enforcement/ganchos; (4) `feat(cashshop): catalog warmup` — K1; (5) `test(cashshop): expiry integration suites` — 6 fixtures; (6) `feat(custommove): v2 batch integration` — 8 arquivos + testes + csproj; (7) `chore(monitor): fix gap counting` — summarize.ps1 (fora do repo, arquivo local); (8) `docs(cashshop): release validation` — 2 docs + runner.

## 2. Reconciliação de migrations POR IDENTIDADE

Assembly RC1: **58 migrations** (lista completa em `C:\Windows\Temp\rc1-asm-migrations.txt`; última: `20261022071625_AddCashShopItemExpiry`).

| Banco | Aplicadas | Em histórico e AUSENTES da RC1 | Pendentes do upgrade RC1 |
|---|---|---|---|
| `openmu` | 50 | `20260919172300_AddCashShopCatalog` (órfã da base antiga) | 9 — **fora do escopo** (a RC1 nunca aponta para `openmu`) |
| `openmu_cashshop_validation` (ALVO) | 58 | `20261012050535_AddJewelBankTables` (aplicada pela sessão JewelBank; não rastreada) | **exatamente 1: `20261022071625_AddCashShopItemExpiry`** |
| `openmu_gm_validation` | 59 | `20261012050535_AddJewelBankTables` + `20261021050451_AddCharacterGameMasterGrantSource` | 1 (a mesma expiry) — frente GM separada |

Divergência compreendida: as tabelas JewelBank ficam órfãs no banco para a RC1 (o modelo não as mapeia; nenhuma FK do fluxo Cash Shop é afetada) — comprovado pelo ensaio na cópia (upgrade + runner **0 desvios**). **Proibido** rodar `dotnet ef migrations add` no worktree contra esse banco (geraria DROP das tabelas JewelBank). As migrations GM **não** são portadas automaticamente.

Schema final esperado na homologação pós-upgrade: modelo RC1 (59 no histórico) + tabelas JewelBank órfãs + colunas `ActivatedAtUtc`/`ExpiresAtUtc`/`DurationSeconds` todas NULL.

## 3. Backup validado

- Arquivo: `C:\Dev\MuOpenSource\backups\openmu_cashshop_validation_pre_rc1_20261024_200259.dump` (custom, 1.269.082 bytes)
- **SHA256: `7DC77306BAE607BC39A1400555FD7733618ED8A6852F2D154D1E10118E6AA23B`**
- Restauração de teste: `openmu_backupverify_350eff2a` — pg_restore `--exit-on-error` exit 0; conferência: **5 contas / 4 personagens / 4 linhas de storage / 193.500 W Coin(P)** — idêntico à origem. Ownership preservado (`--no-owner` NÃO usado no restore de verificação; grants intactos).
- Nenhum restore no banco compartilhado. Nenhum serviço alterado.

## 4. Compose e CustomMove (configuração efetiva encontrada)

- Base: `deploy/all-in-one-traefik/docker-compose.yml` — openmu com portas `55901-55903`, `44406`, `55980`; `DB_HOST: database` (sobrescrito pelo override).
- Override vigente: `C:\Windows\Temp\openmu-traefik-homologacao.compose.yml` — `DB_NAME=openmu_cashshop_validation`, `DB_HOST=host.docker.internal`, `DB_ADMIN_USER/PW=postgres`, `RESOLVE_IP=127.127.127.127`, `OPENMU_CASH_SHOP_REOPEN_PACKAGES=1` (**exclusivo da homologação** — não promover), `build:` apontando ao worktree com `image: gift-perf-2ad` (ANTIGO — é isso que o deploy substitui).
- **Override da RC1 preparado (não aplicado)**: `C:\Windows\Temp\openmu-traefik-rc1.compose.yml` — `image: munique/openmu:cashshop-phase10-rc1`, sem bloco `build`, demais variáveis preservadas. Rollback imediato = reverter ao override de homologação.
- Plugins CustomMove: TypeId `b7a3c9d2-e5f1-4a8b-9c7d-3e6f2a1b4c5d` persiste em `config."PlugInConfiguration"` (não depende da imagem); RC1 inclui o v2 (`/custommove` + fallback de alias + validações).
- Conexões: container `Up`; verificação de jogadores online é passos 1–2 da janela (pelo painel/logs do container).

## 5. Evidências da imagem (registradas; nada reexecutado além do necessário)

Regressão da rodada de integração (código = contexto exato da build): OpenMU.Tests **719/719**, Packets **493/493**, CustomMove+CashShop **229/229**, efêmeros EF (58 migrations + upgrade de cópia da homologação) com **0 desvios** e mediana 52 ms no claim fallback; expiração online (timer real) e offline (re-log) GREEN; itens legados preservados (fingerprint + NULLs).

## 6. Janela de homologação — sequência executável (NÃO executar nesta rodada)

```powershell
# 1) Autorização + zero jogadores (painel/logs: docker logs openmu --since 10m)
# 2) Registrar imagem anterior (já registrado): ab57829c5abe…custommove-fix-20261018
# 3) Registrar configuração anterior: Copy-Item C:\Windows\Temp\openmu-traefik-homologacao.compose.yml C:\Windows\Temp\openmu-traefik-homologacao.compose.yml.bak-prerc1
# 4) Backup já feito (seção 3) — refazer se a janela ocorrer noutro dia
# 5) Suspensão de transações Cash Shop (mecanismo disponível):
#    UPDATE data."CashShopPackage" SET "SalesFlag" = 0 WHERE "DeleteFlag" = 181;   -- reversível
# 6) Aplicar somente a migration pendente (a partir do HOST, DB_NAME=homologação):
#    $env:DB_NAME='openmu_cashshop_validation'; dotnet ef database update --context EntityDataContext --project src\Persistence\EntityFramework\MUnique.OpenMU.Persistence.EntityFramework.csproj --startup-project src\Persistence\EntityFramework\MUnique.OpenMU.Persistence.EntityFramework.csproj
#    (aplicará exatamente 20261022071625_AddCashShopItemExpiry — ensaiado em cópia)
# 7) Substituir o override e recriar SOMENTE o openmu com a RC1:
#    Copy-Item C:\Windows\Temp\openmu-traefik-rc1.compose.yml C:\Windows\Temp\openmu-traefik-rc1-active.compose.yml
#    docker compose -f C:\Dev\MuOpenSource\OpenMU\deploy\all-in-one-traefik\docker-compose.yml -f C:\Windows\Temp\openmu-traefik-rc1-active.compose.yml up -d openmu
# 8) Confirmar: docker inspect openmu --format '{{.Image}}' == 2512867e09fd… ; docker logs openmu (startup sem erro; migrations aplicadas; porta 44406)
# 9) Catálogo + warmup: docker logs openmu | Select-String 'warmup|catalog'
# 10–13) Main.exe: compra D2 03, gift D2 04, storage D2 05/0E, claim D2 0B (roteiro §3 do phase10-release-validation.md)
# 14) Reconciliação (queries da seção 7 abaixo)
# 15) Frio/aquecido: [buy-trace] resolver X ms na 1ª compra pós-boot vs seguintes
# 16) CustomMove: /custommove bar no cliente + painel /plugins (banner PluginActive)
# 17–19) GO/NO-GO -> commits/tag somente após homologação visual
```

## 7. Consultas de reconciliação (pós-deploy)

```sql
select count(*) from "__EFMigrationsHistory";                                   -- esperado: 59
select count(*) from information_schema.columns where table_schema='data'
  and column_name in ('ActivatedAtUtc','ExpiresAtUtc','DurationSeconds');       -- esperado: 3
select coalesce(sum("Balance"),0) from data."WCoinWallet";                      -- esperado: 193500
select count(*) from data."CashShopStorageItem";                                -- esperado: 4 (antes da 1ª entrega)
select count(*) from data."Item" where "ActivatedAtUtc" is not null;            -- 0 antes da 1ª entrega; N após
select "Id"::text, "ActivatedAtUtc", "ExpiresAtUtc" from data."Item"
  where "ExpiresAtUtc" is not null;                                             -- delta = duração contratada
```

## 8. Rollback

- **Antes de qualquer item temporário**: reverter o override (`openmu-traefik-homologacao.compose.yml`) e `up -d openmu` com a imagem anterior (`custommove-fix-20261018`, `ab57829c5abe…`). A coluna extra no banco é ignorada pela imagem antiga — restaurar o backup só se houver corrupção de dados.
- **Depois de itens temporários ativos**: rollback para imagem sem expiração é **PROIBIDO** (itens expirados voltariam a ser utilizáveis e prazos seriam ignorados). Recuperação = **imagem compatível** (a própria RC1 com correção aplicada à frente — migration aditiva, sem Down) e, se os dados forem corrompidos, restauração do backup verificado (seção 3) NUMA IMAGEM COMPATÍVEL. Down de `20261022071625` é proibido com itens ativos.
- Suspensão de transações durante recuperação: `UPDATE data."CashShopPackage" SET "SalesFlag" = 0 WHERE "DeleteFlag" = 181;` (reverter para 182 após).

---

## 9. RELATORIO OPERACIONAL FINAL (deploy executado em 2026-10-24/25, janela autorizada)

1. PRE-FLIGHT: Docker 29.4.3 estavel; imagem anterior ab57829c5abe... (custommove-fix-20261018, Up, restarts=0); RC1 2512867e09fd... presente; projeto Compose = all-in-one-traefik, servico = openmu, arquivos = docker-compose.yml + openmu-traefik-homologacao.compose.yml + openmu-custommove-override.yml (este ultimo so fixa a imagem antiga — substituido pelo override RC1, que ja contem o lote CustomMove v2); jogadores online = 0 (zero TCP ESTABLISHED na 44406; ultima atividade de log 19:59:19); banco-alvo confirmado = openmu_cashshop_validation (58 migrations, JewelBank preservada, expiry ausente). RESULTADO: PASS (achado: erro recorrente pre-existente de AppearanceSerializerExtended span 18 vs 27 na imagem antiga, registrado para a sessao CustomMove).
2. BACKUP: openmu_cashshop_validation_pre_rc1_deploy_20261024_232105.dump (1.269.082 B) SHA256 35557E574F012F337A72D9203ABC941D55984078E7FC766881C5A0984B602C1A; restore-teste openmu_deployverify_* exit 0; contagens verificadas 58/5/193500/4/1024 e 4/4 storage GUIDs. RESULTADO: PASS.
3. MIGRATION: comando = dotnet ef database update 20261022071625_AddCashShopItemExpiry --context EntityDataContext --project src\Persistence\EntityFramework\MUnique.OpenMU.Persistence.EntityFramework.csproj --startup-project src\Persistence\EntityFramework\MUnique.OpenMU.Persistence.EntityFramework.csproj (DB_NAME=openmu_cashshop_validation); DbContext = EntityDataContext; database confirmado por current_database(); pendencia previa = exatamente 1; historico 58 -> 59; expiry_once=1; JewelBank preservada; 3 colunas com tipos corretos; wrings=3 intactos; activated_items=0; retro_duration=0. RESULTADO: PASS.
4. DEPLOY: comando = docker compose -p all-in-one-traefik -f <base> -f openmu-traefik-homologacao.compose.yml -f openmu-traefik-rc1.compose.yml up -d --no-deps --no-build --force-recreate --pull never openmu; servicos recriados = APENAS openmu; imagem executada = 2512867e09fd... (exata); DB_NAME = openmu_cashshop_validation; portas preservadas (44406/44405/55901-55906/55980); restarts=0; Game Server 0 initialized; warmup concluido no boot (03:47:59, ~63s apos o start — custo frio deslocado para o boot, conforme K1). RESULTADO: PASS.
5. INTEGRIDADE: contas 5->5, personagens 4->4, WCoin(P) 193500->193500, storage 4->4 (4 chaves distintas, sem duplicados), itens 1024->1024, 3 Wizard Rings preservados, activated=0, retro=0; openmu=50 e openmu_gm_validation=59 intocados. RESULTADO: PASS.
6. FUNCIONALIDADES: Cash Shop inicializada (catalogo carregado); Gift/Claim/compras = aguardando Main.exe; expiracao = enforcement + varredura ativos (provados nos testes da mesma base de codigo); warmup = executado sem erro; CustomMove = plugins carregados sem erro (validacao visual pendente); regressoes = nenhuma observada nos logs. RESULTADO: PASS (funcional transacional pendente do cliente).
7. MAIN.EXE: PENDENTE — checklist pronto (phase10-release-validation.md §3); exige interacao fisica do usuario.
8. RECUPERACAO: imagem anterior preservada (ab57829c5abe); migration aplicada; ITENS TEMPORARIOS ATIVADOS = 0 ate agora (rollback para imagem antiga AINDA permitido tecnicamente, pois nenhum item temporario existe; APOS a primeira ativacao, proibido); backups disponiveis (pre_rc1_20261024_200259 + pre_rc1_deploy_20261024_232105); plano = Gate R do predeploy-gonogo.md.
9. PRESERVACAO: openmu=50 intocado; openmu_gm_validation=59 intocado; JewelBank tabelas orfas preservadas; OpenMU principal intocado; MuMain intocado; Traefik intocado; monitor intacto; git = 62 pendencias (nada commitado); push/tag = nao executados.

STATUS: DEPLOY RC1 CONCLUIDO — INTEGRIDADE APROVADA, HOMOLOGACAO VISUAL PENDENTE (ultimo gate aprovado: 6; Gate 7 aguarda o usuario).
