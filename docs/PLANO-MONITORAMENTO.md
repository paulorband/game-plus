# Plano: Monitoramento (Afterburner + RTSS)

Status: **implementado (fases 1-6); falta validar o OSD sobre um jogo real e no ROG Xbox Ally X.**

## Objetivo

Categoria "Monitoramento" no overlay do Game Plus, operável só com o controle, para ligar/desligar métricas exibidas sobre o jogo:

| Grupo | Métricas |
|---|---|
| CPU | temperatura, uso (%), frequência, consumo (W), tensão |
| RAM | consumo, frequência |
| GPU | temperatura, uso (%), frequência, uso de VRAM, consumo (W), tensão |
| FPS | FPS atual (com/sem gráfico), média (AVG), frametime (com/sem gráfico) |

Além disso: escolha da GPU monitorada, posição na tela (grade 3×3) e tamanho do texto.

## Decisões tomadas

1. **Abordagem sem administrador**: sensores lidos da memória compartilhada do Afterburner e overlay desenhado pelo Game Plus na memória compartilhada do RTSS. Não editamos arquivos do Afterburner/RTSS.
2. **GPU**: a tela lista as GPUs disponíveis e o usuário escolhe (seletor `‹ nome ›`). Padrão sugerido: a de maior atividade.
3. **FPS**: medir o FPS **do jogo** (processo do jogo no RTSS). O liga/desliga do overlay de geração de quadros já existe na tela do Lossless Scaling.

## Achados técnicos (medidos neste PC, Ryzen 7 9700X + RTX 5060 Ti + iGPU AMD)

- `Program Files\MSI Afterburner\Profiles\MSIAfterburner.cfg` e `RTSS\Profiles\Global` **não são graváveis sem administrador**, e o Afterburner só relê o config ao iniciar. Por isso não editamos esses arquivos.
- `MAHMSharedMemory` (Afterburner 4.6.6): abre para leitura sem admin. Entradas por sensor (`CPU temperature`, `CPU usage`, `CPU clock`, `CPU power`, `RAM usage`, `GPU1/GPU2 ...`, `Framerate`, `Framerate Avg`, `Frametime`). Cabeçalho de 32 bytes, entrada de 1324 bytes, nome em 260 bytes, valor `float` em +1300. Valor inválido = `FLT_MAX` (~3.4e38).
- `RTSSSharedMemoryV2` (RTSS 7.3.5, v2.15): abre para leitura **e escrita** sem admin. 8 espaços de OSD. Suporta texto com tags (`<P>`, `<S>`, `<C>`) e objetos embutidos de gráfico (`RTSS_EMBEDDED_OBJECT_GRAPH`, com flags de framerate e frametime). Headers em `RivaTuner Statistics Server\SDK\Include` e `MSI Afterburner\SDK\Include`.
- Disponibilidade das métricas aqui: CPU temp/uso/freq/consumo OK; **tensão da CPU inválida**; RAM consumo OK; **RAM frequência não existe no Afterburner** (obter por WMI `Win32_PhysicalMemory.ConfiguredClockSpeed`, sem admin); GPU temp/uso/freq/VRAM/consumo OK; tensão da GPU não válida; FPS/frametime só com jogo rodando.
- Há **duas GPUs** neste PC (`GPU1` = iGPU AMD, `GPU2` = NVIDIA). No Ally há uma só.

## Fases

1. **Design** — atualizar o design system com a tela Monitoramento (protótipo interativo aprovado antes de codar; ver `CentralDeJogoOverlayDesign\monitoramento-prototipo.html`).
2. **Leitura de dados** (`Services/`)
   - `MahmReader`: lê sensores do Afterburner; expõe valor e disponibilidade (`FLT_MAX` = indisponível); lista as GPUs.
   - `RamInfoProvider`: frequência da RAM via WMI (cache; muda pouco).
   - `FpsReader`: FPS, média e frametime do processo do jogo pelo RTSS (não depende do Afterburner).
3. **Desenho do overlay**
   - `RtssOsdWriter`: escreve texto no espaço próprio do RTSS (nunca o do Afterburner), atualiza a cada ~500 ms, controla posição/tamanho por tags, gráfico de FPS/frametime por objeto embutido. Libera o espaço ao desligar/sair.
   - Recomendar desligar o OSD do próprio Afterburner para não duplicar.
4. **Configuração**: `Data\monitoring.json` ao lado do `.exe` (portátil): métricas ligadas, GPU escolhida, posição (0–8), tamanho (50–150%).
5. **Tela no overlay**: categoria Monitoramento em `Overlay/` — cartão principal (liga/desliga a exibição, mostra estado do Afterburner/RTSS), seções GPU/CPU/RAM/FPS/Aparência, novo tipo de linha `PositionRow` (grade 3×3), reutilizando `BoolSettingRow`, `EnumSettingRow` e slider. Métricas indisponíveis ficam esmaecidas com o motivo.
6. **Estados de erro**: Afterburner/RTSS ausentes ou fechados → alerta no cartão (como no Lossless Scaling), sem botão de ligar.
7. **Validação**: teste com o Witcher 3 aqui e no ROG Xbox Ally X; `publish`; atualizar `CLAUDE.md` e README; commit.

## Riscos

- Aparência (tamanho/posição por tags do RTSS) precisa ser validada visualmente.
- O RTSS mede o jogo, não os quadros gerados pelo Lossless Scaling (aceito: mostramos FPS do jogo).
- Leitura de memória compartilhada é código nativo sensível ao layout das estruturas; testar com versões diferentes do Afterburner/RTSS e tratar assinatura/versão desconhecida como "indisponível".
- O Ally X pode ter outro conjunto de sensores (uma só GPU, sem tensões); a interface já trata "indisponível".
- Afterburner/RTSS podem não estar instalados no Ally: tratar como estado normal, não como erro.
