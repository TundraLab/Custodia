# Histórico de Versões (Changelog)

Todas as mudanças principais do Custódia seguem versionamento semântico (`MAJOR.MINOR.PATCH`).

## [2.9.1] - 2026-11-24

### Alterado
- **Ícone e menu dinâmicos na bandeja redesenhados:** animação pulsante com variação real de tamanho (56% a 100%) e glifos centralizados de alto contraste (`↑` azul, `↓` verde, `✕` vermelho, setas circulares `↻` e alerta `!`) para leitura clara na barra superior do sistema.
- **Gestão visual de `credentials.json` e contas por diretório próprio:** armazenamento organizado em `~/.config/custodia/contas/<email>/`, detecção automática na pasta Downloads, suporte a arrastar e soltar `.json` em **Configurações**, visualização do `Project ID` / `Client ID` e ações rápidas por conta (`Alternar`, `Reconectar`, `Trocar JSON`, `Pasta`, `Revogar`).
- **Configuração de pastas salva por conta:** as pastas mapeadas permanecem vinculadas ao e-mail da conta caso ela seja removida e adicionada novamente, com opção explícita para excluir essa configuração durante a remoção.
- **Suporte unificado com seletor `Bug / Erro` vs `Sugestão` e diagnóstico opcional sanitizado:** seletor no topo da seção aplicando automaticamente `&labels=bug` ou `&labels=enhancement`, adaptação dos campos do formulário, opção desmarcada por padrão para incluir ambiente e últimos avisos/erros (com mascaramento de e-mail/usuário, prévia via botão **Ver dados** e corte seguro da URL preservando o log completo em **Copiar texto**).

### Corrigido
- **Status travado no menu da bandeja com fila zerada:** limpeza imediata de direções temporárias quando não há sincronização ou transferências ativas e timer coalescente no menu nativo para evitar que status antigos (como `✕ Excluindo arquivo...`) fiquem congelados na barra de notificação.
- **Envio de avisos no Gmail ao conectar, reconectar ou desconectar:** resolução da credencial por conta alvo (inclusive secundárias), codificação Base64 UTF-8 do corpo HTML, timeout ampliado para 8s e atalho **Ativar Gmail API** em **Configurações**.

## [2.9.0] - 2026-11-24

### Adicionado
- **Ícone dinâmico na bandeja do sistema e menu com status vivo:** o ícone na barra de notificações anima estados de envio, download, exclusão, sincronização e erro, com indicadores claros no menu da bandeja.
- **Criação de pastas direta e mapeamento em lote sem nomear manualmente:** novo botão **Nova Pasta** em **Meus Arquivos** (modo remoto e local) e no **Navegador Remoto**, além de seleção múltipla de pastas locais e botão **Sincronizar Pasta Local Aqui** mantendo nomes e subpastas automaticamente (inclusive diretórios vazios).
- **Personalização por pasta:** escolha apelido, cor e ícone personalizados em **Pastas de Sincronização**, refletidos também em **Meus Arquivos**.
- **Mensagens e animações de erro de transferência:** alertas visuais na barra lateral, na **Fila de Transferências** (banner animado + destaque no cartão) e em **Meus Arquivos** quando ocorre falha de upload ou download.
- **Variações de som nas notificações:** 6 toques simples sintetizados (com botão **Ouvir**) e opção para silenciar mantendo ou desativando o som das notificações.
- **Avisos por e-mail no Gmail:** opção para enviar alerta automático para a própria conta Gmail ao conectar ou desconectar no Custódia.
- **Agendamento de atualizações, suporte a erros e sugestões na aba Sobre:** exibição da frequência e previsão da próxima checagem, tratamento imediato de erro sem internet e formulários integrados para reportar erros ou enviar sugestões futuras.
- **Notificação dedicada ao concluir sincronização inicial:** alerta próprio quando uma nova pasta adicionada termina sua primeira sincronização.

### Alterado
- **Renomeação oficial de Meu Driver para Meus Arquivos (`My Files` em EN-US):** aplicada na navegação, bandeja, navegador remoto, documentação e site oficial.

### Corrigido
- **Fila de transferências sem teto de 200 itens:** remoção do limite baixo que travava a contagem em 200 itens, preservando o histórico expandido e contadores exatos do serviço.
- **Animação infinita de sincronização quando ocioso:** correção na ordem de atualização de estado em `TriggerFolderSyncAsync` e limpeza de itens órfãos para que a animação pare assim que todas as pastas terminarem.
- **Animação infinita ao verificar atualizações sem internet:** detecção imediata de ausência de rede e encerramento seguro do loop de busca com mensagem de erro clara.

## [2.8.0] - 2026-11-20

### Alterado
- **Novo nome e identidade visual oficial (`Custódia by TundraLab`):** o projeto agora se chama **Custódia** (*"Seus arquivos. Sempre seguros."*), com nova logomarca, novo ícone na bandeja do sistema, novos pacotes `Custodia` (`.rpm` e `.AppImage`) e migração automática das configurações e contas já salvas no computador.
- **Layout da aba Sobre e painel de atualizações ajustado:** os cartões da aba **Sobre** agora aproveitam toda a largura da área principal da janela, o botão **Baixar e atualizar agora** fica posicionado logo acima das notas da versão para acesso imediato e o quadro de novidades conta com rolagem própria, permitindo atualizar o aplicativo confortavelmente sem precisar redimensionar a janela.

## [2.7.4] - 2026-11-20

### Corrigido
- **Inicialização com o sistema sem duplicidade nem fechamento:** ao ligar o computador e entrar na sessão do Linux, o aplicativo aguarda a barra superior do sistema terminar de carregar antes de registrar o ícone na bandeja, evitando o surgimento de dois ícones e o fechamento inesperado logo após o login.
- **Recuperação automática do ícone da bandeja:** caso o painel superior do sistema seja reiniciado ou apresente falha temporária durante o registro do ícone, o Custódia permanece aberto e recria o ícone automaticamente.
- **Limpeza de atalhos duplicados no menu:** instalações via pacote `.rpm` não geram mais um segundo atalho redundante na pasta do usuário.

## [2.7.3] - 2026-11-19

### Melhorado
- **Encerramento limpo ao desinstalar ou atualizar:** ao remover ou atualizar o Custódia pelo terminal (inclusive no formato AppImage rodando na bandeja), qualquer instância aberta é encerrada automaticamente e o ícone da bandeja desaparece na hora.
- **Desligamento gracioso por sinais do sistema:** o aplicativo agora responde corretamente a comandos de encerramento do sistema, fechando a bandeja, a sincronização e o servidor local com segurança.
- **Notas de versão mais legíveis na aba Sobre:** o painel de atualizações formata títulos e marcadores com clareza e sem símbolos de código na tela.

## [2.7.2] - 2026-11-19

### Adicionado
- **Animação de 5 segundos ao buscar atualizações:** ao clicar em **Verificar atualizações** na aba **Sobre**, o aplicativo exibe uma barra de progresso animada com etapas claras e contagem regressiva enquanto consulta as novidades.
- **Verificação automática mensal por padrão:** por padrão global, o Custódia passa a procurar novas versões automaticamente uma vez por mês (a cada 30 dias), com opção nas **Configurações** para escolher entre mensal, quinzenal, semanal ou a cada inicialização.

## [2.7.1] - 2026-11-19

### Melhorado
- **Resumo das novidades antes de atualizar:** as notas da nova versão passam a aparecer diretamente na aba **Sobre** antes de iniciar o download.
- **Identificação da versão na bandeja:** o número da versão instalada agora é exibido ao passar o cursor sobre o ícone da bandeja do sistema.
- **Pasta limpa após instalar ou atualizar pelo terminal:** os instaladores `.rpm` e `.AppImage` baixados durante uma instalação ou atualização são armazenados temporariamente e removidos ao final do processo, preservando a opção de apenas baixar os pacotes quando desejado.

## [2.7.0] - 2026-11-18

### Adicionado
- **Janela única garantida:** tentar abrir o Custódia novamente enquanto ele já está ativo (inclusive minimizado na bandeja) traz a janela existente para frente em vez de abrir uma segunda cópia.
- **Atualizador integrado no aplicativo:** checagem manual e automática de novas versões, aviso discreto no topo da janela, notificação no sistema, barra de progresso no download, conferência de autenticidade e instalação assistida para pacotes `.rpm` e `.AppImage`.
- **Opções extras no instalador de terminal:** detecção automática da versão mais recente disponível e novo comando de atualização direta para instalações existentes.

### Alterado
- Exibição automática da versão em uso em todo o aplicativo sem depender de texto fixo.
- Remoção de menções antigas a pacote `.deb` na documentação do repositório.

## [2.6.0] - 2026-11-18

### Adicionado
- **Seleção múltipla em Meu Driver:** agora você pode selecionar vários arquivos de uma vez nos modos Lista e Grade usando caixas de seleção, `Ctrl + Clique`, `Shift + Clique` ou `Ctrl + A`, além de realizar ações em lote (baixar, mover para a Lixeira, restaurar, excluir permanentemente e enviar múltiplos arquivos).
- **Animações e cores por tipo de transferência:** envios em azul, downloads em verde e exclusões em vermelho na barra lateral e na Fila de Transferências, com exibição clara mesmo em arquivos rápidos e painel de detalhes ao clicar em cada item.
- **Notificações nativas com ação rápida:** avisos de sincronização integrados ao sistema com botão **Abrir pasta** direto no gerenciador de arquivos e filtro automático para evitar alertas repetidos quando nada foi alterado.
- **Compatibilidade ampliada com ambientes Linux:** funcionamento ajustado para KDE Plasma 5 e 6 (diálogos do sistema, atalhos, Dolphin e Lixeira), além de GNOME e COSMIC (Pop!_OS).
- **Instalador interativo no terminal:** novo assistente oficial para instalar, atualizar ou remover o aplicativo em pacotes `.rpm` e `.AppImage`, com verificação de autenticidade.
- **Fluxo dedicado para novas contas:** ao adicionar outra conta Google, o aplicativo solicita primeiro o arquivo de credenciais dessa conta antes de abrir a autorização no navegador.
- **Prévia visual e guia de configuração:** capturas de tela e passo a passo ilustrado para configurar o acesso ao Google Drive adicionados à documentação.

### Segurança
- **Proteção contra caminhos inválidos:** validação rigorosa no download, na leitura de subpastas e no envio de arquivos para impedir acessos fora das pastas sincronizadas.
- **Proteção reforçada na comunicação local:** verificação de origem nas requisições recebidas localmente e bloqueio automático de credenciais criadas para sites em vez de aplicativos de computador.

### Corrigido
- **Restauração confiável pela bandeja:** clicar no ícone da bandeja do sistema agora volta a exibir a janela minimizada corretamente no GNOME, Pop!_OS e KDE Plasma.
- **Atalhos e inicialização automática no AppImage:** geração correta do atalho no menu de aplicativos e na inicialização com o sistema ao usar a versão AppImage.
- **Limpeza visual das telas:** remoção de subtítulos repetitivos nos cabeçalhos de página.

## [2.5.0] - 2026-11-17

### Adicionado
- **Automação local integrada:** servidor local restrito ao próprio computador para consultas de status e integrações de uso pessoal.
- **Pré-visualização e miniaturas de arquivos:** cache local em disco e visualização integrada para imagens, áudio, vídeo, PDFs e documentos de escritório.
- **Otimização automática de memória:** limpeza periódica e manual do cache de mídia fora da tela para manter o consumo de memória baixo durante longos períodos na bandeja.
- **Suporte a múltiplas contas Google:** base preparada para conectar e alternar rapidamente entre diferentes contas na barra lateral.

### Melhorado
- Listagem mais rápida de pastas remotas e atualização imediata do espaço disponível após downloads ou exclusões.

## [2.4.0] - 2026-11-05

### Adicionado
- **Tela Fila de Transferências:** acompanhamento com barras de progresso em tempo real, cancelamento de tarefas em andamento e botão para limpar itens concluídos.
- **Detalhamento do armazenamento na nuvem:** exibição separada do espaço ocupado por Drive, Gmail / Fotos e Lixeira, com atalho rápido para gerenciar espaço.
- **Troca de idioma instantânea:** alternância entre Português (Brasil) e Inglês em tempo real sem precisar reiniciar o aplicativo.
- **Controle de escala da interface:** ajuste de zoom entre 100% e 300% nas configurações ou pelos atalhos `Ctrl +`, `Ctrl -` e `Ctrl 0`.

## [2.2.0] - 2026-10-18

### Adicionado
- **Navegador Remoto do Google Drive:** exploração das pastas na nuvem antes mesmo de escolher quais sincronizar ou baixar sob demanda.
- **Integração completa com a Lixeira:** visualização de itens apagados no Google Drive, permitindo restaurar arquivos ou excluí-los definitivamente direto pelo aplicativo.
- **Busca e alternância de visualização:** pesquisa rápida por nome ou caminho e troca entre visualização em Grade e Lista.

### Melhorado
- Conversão e exportação amigável de arquivos Google Docs, Planilhas e Apresentações durante o download.

## [2.0.0] - 2026-10-02

### Alterado
- Reorganização estrutural do aplicativo para separar motor de sincronização, serviço local e interface desktop.
- Nova interface desktop nativa para Linux com tema escuro unificado e suporte a sessões gráficas modernas e tradicionais.

### Adicionado
- **Ícone persistente na bandeja do sistema:** permite fechar a janela mantendo a sincronização ativa em segundo plano.
- **Inicialização automática com o sistema:** opção para abrir o aplicativo junto com a sessão do usuário no Linux.
- **Tela Sobre:** exibição de versão, desenvolvedor e atalho para o repositório oficial.

## [1.5.0] - 2026-09-12

### Adicionado
- **Dois modos de sincronização por pasta:** escolha entre **Bidirecional** (mudanças dos dois lados) ou **Somente envio / Backup** (do computador para a nuvem).
- **Regras de exclusão de arquivos:** suporte a filtros para ignorar arquivos temporários, caches e padrões definidos pelo usuário.
- **Verificação de integridade e conflitos:** comparação automática do conteúdo antes de sobrescrever alterações locais ou na nuvem.

### Corrigido
- Prevenção de reenvios desnecessários logo após concluir o download de um arquivo atualizado.

## [1.2.0] - 2026-08-22

### Adicionado
- **Monitoramento contínuo de pastas:** detecção automática de arquivos criados, modificados ou movidos no computador, agrupando edições rápidas em sequência.
- **Histórico local persistente:** registro seguro das contas, pastas mapeadas e estado de cada arquivo entre reinicializações.
- **Tela Logs de Atividade:** consulta detalhada do que foi sincronizado, com filtros por tipo de evento e pesquisa por texto.

## [1.0.0] - 2026-08-01

### Adicionado
- **Lançamento inicial do Custódia para Linux:** primeiro aplicativo desktop para sincronizar pastas locais com o Google Drive.
- **Autenticação segura com credenciais próprias:** conexão direta com a conta Google do usuário e armazenamento protegido das chaves de acesso no computador.
- **Mapeamento inicial de pastas:** vinculação de diretórios locais do Linux a pastas do Google Drive.
