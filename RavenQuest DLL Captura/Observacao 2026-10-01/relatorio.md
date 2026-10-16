# Observação do RavenQuest — 1 de outubro de 2026

O cliente usa muito mais arquivos que as três DLLs visíveis na raiz da instalação. A coleta ETW confirmou **90 DLLs e um EXE presentes no processo do jogo (PID 30348)**. Também registrou aberturas e leituras de recursos e uma mudança real da restrição de captura na mesma janela.

A observação é passiva: nenhum arquivo do jogo foi alterado e nenhuma DLL foi injetada. A coleta foi encerrada.

## O que foi observado

- Coleta ETW válida: **2026-10-01T16:33:13.302783-03:00 a 2026-10-01T16:33:43.795290-03:00**, aproximadamente 30 segundos. Eventos perdidos informados: **0**.
- **8443 eventos** atribuídos ao cliente e aos auxiliares RavenQuest/BattlEye: 7410 eventos de arquivos, 605 de rede, 16 do Registro, 404 de imagens executáveis e 8 de processos.
- Processos vistos: ravenquest_dx.exe (30348), ravenquest_dx_BE.exe (13112), BEService.exe (29560) e crashpad_handler.exe (22608).
- DLLs do cliente incluem libEGL.dll, libGLESv2.dll e **BattlEye/BEClient_x64.dll**; também D3D12.dll, D3D12Core.dll, dwmapi.dll, dxgi.dll, nvspcap64.dll, componentes de drivers NVIDIA e bibliotecas do Windows.
- Os registros de imagens são Image/DCStart e Image/DCStop: levantamentos de módulos já presentes. **Não são evidência de um novo carregamento durante esses 30 segundos**, nem de uma função específica executada.
- O executável declara 34 nomes de dependências na tabela PE. Isso é diferente das 90 DLLs presentes: há dependências transitivas e carregamentos em execução.

## Acessos reais a recursos

O cliente tentou localizar images/game/targetselector/white96.png na instalação, no AppData e nas variantes mods. Esses caminhos retornaram NTSTATUS 0xC000003A (caminho ausente). Depois, abriu com sucesso:

`C:/Program Files (x86)/Tavernlight Games/RavenQuest/data/images/game/targetselector/white96.png`

Também foram observadas leituras de imagens da interface, por exemplo data/images/ui/windows/health_bars/aether_animation/9/03.png. Esses eventos comprovam busca e leitura de recursos. As tentativas sem sucesso são parte do mecanismo de procura; não demonstram por si só falha ou crash do jogo.

O log ativo no AppData cresceu e config.otml mudou durante a observação anterior (16:18:15–16:18:43). O identificador do BattlEye foi omitido do registro de log exportado. O log da instalação e o registro antigo de crash não mudaram naquele intervalo.

## Restrição de captura

- Às **16:21:26**, HWND 0x150EFA, PID 30348: consulta bem-sucedida, **0x0**.
- Às **16:39:04**, a mesma janela e PID: consulta bem-sucedida, **0x1**.
- Portanto, o estado mudou de WDA_NONE para WDA_MONITOR dentro desta sessão. Só temos esses pontos de consulta; o instante exato da mudança não foi registrado.
- **Não foi identificada a função chamadora nem a DLL responsável**. BEClient_x64.dll presente no processo não é prova de que ela efetuou a alteração.

## Consequência para CaptureRestore.dll

Não há referência a CaptureRestore.dll nos eventos examinados. Não houve abertura/leitura dela no intervalo e ela não aparece na lista de módulos presentes. A lista de bibliotecas existentes e a busca por imagens em mods/AppData **não demonstram suporte a carregar automaticamente qualquer DLL nessas pastas**.

Não foi confirmado um local de instalação para nossa DLL. Ela continua precisando de um mecanismo que a carregue no processo do cliente e invoque seu export. Esta observação não realizou essa integração.

## Cobertura e limites

- O cliente começou a executar às 16:16:26. A captura válida começou depois, com ele já aberto. **Não temos todos os acessos desde o clique em abrir**.
- FileIO/Create pode representar abrir um arquivo existente; não significa necessariamente criar um arquivo novo. Consultas de metadados não comprovam leitura de conteúdo.
- Há **184 eventos de arquivo sem caminho resolvido** no conjunto de alvos. Eles não são convertidos em nomes presumidos.
- Alguns eventos somente Close têm nomes de arquivo cuja atribuição exige revisão, incluindo caminhos do runtime Codex. Eles não foram usados para afirmar que o jogo abriu DLLs ou leu esses arquivos. game_file_open_read_write.csv limita o subconjunto revisado a Create/Read/Write.
- Metadados de rede e nomes de chaves do Registro foram coletados; nenhum conteúdo de tráfego, chat, valor de Registro, senha ou dump de memória foi exportado.
- O WPR original falhou ao finalizar com 0x80010106. O registro válido veio da coleta ETW direta; nenhum sucesso de exportação foi presumido na tentativa do WPR.

## Arquivos da entrega

- game_loaded_modules.csv: DLLs/EXE presentes no cliente, deduplicados.
- game_file_open_read_write.csv: operações Create/Read/Write atribuídas ao cliente.
- accessed_paths.csv e CSVs por categoria: metadados filtrados dos processos alvo; considerar os limites acima.
- startup_analysis.json e findings.json: contagens e resultados.
- process_observation.json, module_observation.json, network_observation.json, window_observation.json, window_followup.json e log_observation.json: observações complementares.
- Os ETLs brutos permanecem no workspace. Eles contêm metadados de todo o sistema; os relatórios exportados foram filtrados para os alvos.

## Referências técnicas

- [Microsoft: contrato de SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Microsoft: diagnóstico do erro WPR 0x80010106](https://devblogs.microsoft.com/performance-diagnostics/wpr-start-and-stop-commands/)
- [Microsoft TraceEvent: implementação da sessão](https://github.com/microsoft/perfview/blob/v3.2.8/src/TraceEvent/TraceEventSession.cs)
