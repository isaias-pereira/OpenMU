# Fase 10 — Encerramento Final (2026-11-01)

Registro definitivo de encerramento da Fase 10 (Cash Shop/XShop). Executado com
ferramentas reais: build x86 Release completo, testes doctest reexecutados e
hashes conferidos, sem repetir homologações anteriores já registradas.

## 1. Commits OpenMU — branch `checkpoint/cashshop-phase10-isolated` (HEAD `f70617671`)

| Commit | Descrição |
|---|---|
| `3012c6452` | feat(cashshop): complete phase 10 gift claim storage and expiry |
| `2a87e3c69` | feat(custommove): isolate commands and preserve native move |
| `f70617671` | fix(cashshop): enforce temporary item expiry across gameplay |

Origin: `https://github.com/isaias-pereira/OpenMU.git`. Nenhum push efetuado
nesta rodada (ver seção 7). Metastate do worktree: alterações locais X de
sessões anteriores permanecem não commitadas por autorização — não foram
incluídas em nenhum commit nem no push.

## 2. Commits MuMain — branch `cashshop-skinfix-integration`

| Commit | Descrição |
|---|---|
| `2834a807f` | fix(cashshop-client): preserve shop encoding and gift message |
| `21b91de9a` | fix(transform): correct local and remote ring transformations |
| `40f1560f3` | **Commit complementar** (novo nesta rodada): restaura o dispatch moderno de pacotes e o tratamento de caminhos upstream em `WSclient.cpp`/`ShopList.cpp`, preserva a validação `safe_cast` de tamanho de pacotes, mantém as correções F1/F2/F3 (`ZzzCharacter.cpp`) e corrige a **captura do Gift pelos campos two-way-bound da interface Rml** (`MsgBoxIGSSendGift.cpp` — `OKButtonDown` captura `recipient`/`message` incondicionalmente, eliminando o envio de mensagem vazia) |

Base validada: `3f326ffbe` (upstream sven-n/MuMain). Origin: sven-n/MuMain
(NADA foi enviado para lá). Fork próprio configurado:
`https://github.com/isaias-pereira/MuMain.git` (remote `fork`).

### 2.1 Checklist Tarefa A — aprovado nos 6 itens
1. Validações modernas de pacotes preservadas: `safe_cast` + `LogSafeCastSizeMismatch` intactos; dispatch `ProcessPacket` restaurado ao padrão upstream (span + verificação de tamanho em F3 03/F3 06 etc.).
2. Caminhos da loja: `CShopList::LoadCategroy/LoadPackage/LoadProduct/IsFileEncodingUtf8` usam `mu_narrow_path` + decode UTF-8/CP949 com BOM.
3. F1/F2/F3 intactos: `ZzzCharacter.cpp` (F3 Hero) com guarda de subtipos conhecidos (SKELETON_CHANGED/PANDA/XMAS2008_SNOWMAN).
4. GiftMessage adaptado à Rml: captura incondicional no OK + `SyncRmlModel` + `BindRmlModel` (`igs_send_gift.rml`, maxlength 10, `MAX_GIFT_MESSAGE_SIZE`).
5. Sem alterações upstream descartadas: diffs restauram a base upstream e reaplicam apenas ajustes pontuais.
6. Nenhum arquivo estranho: commit com exatamente 4 arquivos (ZzzCharacter.cpp, MsgBoxIGSSendGift.cpp, ShopList.cpp, WSclient.cpp).

## 3. Gift pela interface Rml (Tarefa B) — PASS (verificação de código; sem envio real)

Fluxo verificado em código, sem compras nem Gift reais (conforme restrição):

1. Botão de Gift → `CreateMessageBox(MSGBOX_LAYOUT_CLASS(CMsgBoxIGSSendGiftLayout))` (MsgBoxIGSBuyPackageItem.cpp:163 / MsgBoxIGSBuySelectItem.cpp:243).
2. Evento Rml → `data-model="igs_send_gift"`; botões `data-event-click="igs_dialog_button(i)"` → `IgsDialogModel.h:34` → `m_PressedButton` → `Update()` → `MSGBOX_EVENT_USER_COMMON_OK/CANCEL`.
3. Captura do destinatário → `model.recipient` → `m_szID` (campo `maxlength="10"` = `MAX_USERNAME_SIZE`).
4. Captura da mensagem → `model.message` → `m_szMessage` (`MAX_GIFT_MESSAGE_SIZE`, textarea).
5. Codificação → Rml string UTF-8 → `StringUtils::NarrowToWide` (UTF-16) → `ShowIGSSendGiftConfirmDialog` → `MU_C16(...)` → `SendCashShopItemGiftRequest` (bind managed, assinatura com `const char16_t*`).
6. Construção/envio → `PacketFunctions_ClientToServer.cpp:802` → símbolo gerenciado `SendCashShopItemGiftRequest` (D2 04; layout já homologado na fase 10 — o client library usa o mesmo gerador de `MUnique.OpenMU.Network.Packets`).

**Teste presencial único (se desejado)** — roteiro mínimo: abrir XShop → pacote de item → Gift → preencher destinatário e mensagem → OK → confirmar (não há envio automático); no servidor `openmu_cashshop_validation` verificar a linha `data."CashShopStorageItem"` com `GiftSender`/`GiftMessage` corretos; destinatário reclama o item (D2 0B).

## 4. Build e testes (Tarefa C) — PASS

| Item | Resultado |
|---|---|
| Build x86 Release (MSVC, Ninja Multi-Config, vcpkg Build Tools, branch de integração) | **PASS** — 1425/1425 steps, 0 erros (apenas warnings C4530 pré-existentes) |
| Fonte do build | Exclusivamente worktree @ `40f1560f3` (branch `cashshop-skinfix-integration`), deps do vcpkg manifest |
| Arquitetura | x86 confirmada no PE (Machine `0x14c`) |
| Testes viewport/aparência — `test_transform_viewport_entry` | **4/4** PASS (24 assertions) |
| Testes — `test_transform_appearance` | **9/9** PASS (125 assertions) |
| Total | **13/13 PASS** (0 falhas, 0 skipped) |
| **Main.exe novo** (não homologado, não substitui o anterior) | SHA-256 `88b27eed0f505d5e4a0c382990ed242fef630dd0a91d682e78fa4c8fd66f1136` |

Os 13 testes compilam com `DOCTEST_CONFIG_NO_EXCEPTIONS_BUT_WITH_ALL_ASSERTS`
(núcleo do cliente é compilado com exceções desabilitadas); o define foi
injetado apenas no build de verificação e não altera o repositório.

## 5. Upstream (Tarefa D) — pendência registrada, NÃO atualizado

O upstream sven-n/MuMain avançou ~30 commits além da base validada `3f326ffbe`
(HEAD remoto verificado: `f81fb1b2ce68055f0a72faeba1ec88a7853dc58f`). Por decisão
da missão, a Fase 10 foi concluída sobre a base já validada. **Atualização do
upstream = pendência independente** (não fazer rebase/merge nesta rodada).

## 6. VPS e ambientes — NÃO modificados

Nenhum deploy na VPS, nenhuma migration aplicada, nenhuma alteração em
PostgreSQL, Docker, RC2 ou no Main.exe homologado anteriormente. O repositório
original `C:\MuMain` permanece intacto.

## 7. Publicação GitHub (Tarefa E) — preparada; push mediante autorização

- OpenMU: branch `checkpoint/cashshop-phase10-isolated` → `origin` (isaias-pereira/OpenMU). Dry-run OK.
- MuMain: branch `cashshop-skinfix-integration` → remote `fork` (isaias-pereira/MuMain). Dry-run OK (branch nova; sem force push; NADA vai para sven-n/MuMain).
- Autenticação funcional (git push --dry-run negociou com ambos os remotos). Link das branches após publicação:
  - `https://github.com/isaias-pereira/OpenMU/tree/checkpoint/cashshop-phase10-isolated`
  - `https://github.com/isaias-pereira/MuMain/tree/cashshop-skinfix-integration`

## 8. Resultado final

| Etapa | Resultado |
|---|---|
| Commit complementar MuMain | `40f1560f3` |
| Gift Rml | PASS (código; roteiro presencial anexo) |
| Build x86 Release | PASS (1425/1425) |
| Testes | PASS 13/13 (4+9) |
| Push OpenMU | Preparado (dry-run OK) — aguarda autorização |
| Push MuMain | Preparado (dry-run OK, fork) — aguarda autorização |
| Documentação | Atualizada (este arquivo) |
| Fase 10 | **ENCERRADA** (pendências de publicação aguardam autorização do proprietário) |

Próximo desenvolvimento liberado: **JewelBank** (frente paralela já existente).
