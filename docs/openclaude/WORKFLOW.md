# Workflow de Trabalho Multiagente

## Investigação

Use `openmu-codebase-explorer` para localizar os componentes e registrar caminhos, símbolos e fluxo.

## Planejamento

Use `openmu-architect` para transformar os achados em sequência de implementação e riscos.

## Execução

Use o especialista do domínio. Evite dois agentes editando os mesmos arquivos simultaneamente, a menos que o trabalho esteja claramente separado.

## Validação

Sempre passar pelo `openmu-test-validator` quando houver mudança de código. Para mudanças de alto risco, seguir com `openmu-code-reviewer`.

## Delegação segura

O agente principal deve enviar para cada subagente:
1. objetivo exato;
2. contexto relevante;
3. arquivos/escopo já descobertos, quando houver;
4. o que NÃO deve ser alterado;
5. formato esperado do relatório.

## Relatório padrão

Cada subagente deve responder:

- Descobertas
- Arquivos analisados
- Alterações realizadas
- Testes executados
- Riscos/regressões
- Pendências
