# Histórico de mudanças

A evolução do Git Painel, na ordem em que as funcionalidades foram criadas.

## 1. Primeira versão
- Janela nativa do Windows (WinForms) com tema Dracula, sem instalação: um único `.exe`.
- Abre uma pasta pai e encontra todos os repositórios Git dentro dela.
- Lista os arquivos alterados de cada repositório e mostra o diff ao clicar, no modo unificado ou lado a lado.
- Rolagem suave, barras de rolagem próprias, barra de título escura.

## 2. Organização por módulos
- Detecta subprojetos (pastas com `package.json`, `serverless.yml`, `requirements.txt` etc.) e agrupa os arquivos por módulo, por exemplo `apps/cadastro`.
- Estrela para marcar os "meus módulos" e filtro **Todos / Meus módulos**.
- Respeita o `.gitignore`.

## 3. Leitura mais confortável
- Quebra de linha no diff (Alt+Z).
- Atualização automática ao salvar arquivos, fazer commit ou trocar de branch.
- Recuo com linhas-guia, como uma árvore.

## 4. Commit pelo app
- Caixas para marcar arquivos ou módulos inteiros e painel de commit.
- Detecta o padrão de mensagens do repositório (Conventional Commits, com ou sem emoji) e sugere tipo e escopo.

## 5. Fluxo completo de Git
- Commit por trecho: marcar só alguns trechos (`@@`) de um arquivo.
- Desfazer o último commit ainda não enviado.
- Push e pull com confirmação, histórico de commits por repositório ou módulo.
- Cores de sintaxe no diff e realce das palavras que mudaram.
- Busca de arquivos (Ctrl+F).

## 6. Integrações e acabamento
- Menu do botão direito: abrir no Explorador, Bloco de Notas ou VS Code; copiar caminho.
- Seleção e cópia de código no diff (Ctrl+C, Ctrl+A, duplo clique seleciona palavra).
- Painel de commit compacto, com seletor de tipo e escopo.
- Botões **Enviar / Atualizar / Publicar** sempre visíveis quando há algo a sincronizar.
- Barra do app integrada à barra de título da janela.

## 7. Robustez
- Animações nos botões (spinner durante push, pull e commit; efeito de pressionar).
- Proteção contra clique duplo e contra duas operações ao mesmo tempo.
- Erros do Git explicados em português.
- Expandir e recolher tudo.
- Na primeira execução, pergunta a pasta de projetos.
