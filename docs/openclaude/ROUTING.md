# Roteamento dos Subagentes OpenMU

| Sinal da tarefa | Agente primário | Apoio |
|---|---|---|
| “onde fica”, “localizar”, investigação de arquitetura | `openmu-codebase-explorer` | `openmu-architect` |
| refatoração C#, serviços, handlers, commands | `openmu-backend` | `openmu-test-validator` |
| tabela, migration, query, índice, PostgreSQL | `openmu-postgres` | `openmu-backend` |
| plugin, extensão, loading, Docker não inclui plugin | `openmu-plugin` | `openmu-docker` |
| XShop, Cash Shop, 0xD2, Main.exe, moeda in-game | `openmu-cashshop` | `openmu-backend` |
| VIP, WCoin, evento, recompensa, gameplay | `openmu-game-systems` | `openmu-postgres` |
| HTML, CSS, JS, painel GM, API web | `openmu-web` | `openmu-security` |
| Dockerfile, Compose, build, deploy, imagem | `openmu-docker` | `openmu-test-validator` |
| login, sessão, autorização, segredo, endpoint | `openmu-security` | `openmu-web` / `openmu-backend` |
| compilar, smoke test, regressão, validação | `openmu-test-validator` | `openmu-code-reviewer` |
| revisão pré-commit / risco / regressões | `openmu-code-reviewer` | `openmu-test-validator` |

## Regra de prioridade

Quando uma tarefa tem múltiplos domínios, o agente principal deve primeiro usar `openmu-architect` para decompor, salvo quando a tarefa já possui escopo claro e pequeno.
