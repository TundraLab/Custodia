# Solução de Problemas (Troubleshooting)

> 🌐 **Idioma / Language:** **🇧🇷 Português (atual)** | [🇺🇸 English](en/TROUBLESHOOTING.md)

Guia prático para diagnosticar e resolver problemas comuns de instalação, integração com o desktop, autenticação OAuth2 e pré-visualização multimídia.

---

## 1. AppImage não abre (erro de FUSE / `libfuse.so.2`)

Em distribuições mais novas (como Ubuntu 24.04+, Pop!_OS ou Fedora sem `fuse2`), o AppImage pode falhar ao tentar montar a imagem.

**Solução 1 — Executar extraindo temporariamente (sem FUSE):**
```bash
./Custodia-3.0.5-x86_64.AppImage --appimage-extract-and-run
```

**Solução 2 — Instalar o suporte FUSE2:**
- **Fedora:**
  ```bash
  sudo dnf install fuse fuse-libs
  ```
- **Ubuntu / Pop!_OS / Debian:**
  ```bash
  sudo apt install libfuse2
  ```

---

## 2. Ícone na bandeja do sistema não aparece no GNOME

O GNOME Workstation não exibe ícones `StatusNotifierItem` / AppIndicator nativamente sem extensão. *(No KDE Plasma e no Pop!_OS, o suporte já vem ativo por padrão.)*

**Solução (Fedora Workstation / GNOME):**
```bash
sudo dnf install gnome-shell-extension-appindicator
gsettings set org.gnome.shell enabled-extensions "['appindicatorsupport@rgcjonas.gmail.com']"
```
Após habilitar pela primeira vez, faça **logout e login** na sessão GNOME para que a barra superior carregue a extensão.

---

## 3. Notificações D-Bus ou som não funcionam

- **Notificações visuais:** verifique se o daemon de notificações do ambiente gráfico está ativo e se o modo **Não perturbe** não está ligado no GNOME, KDE Plasma ou COSMIC.
- **Toques sonoros:** o Custódia possui 6 variações de toques sintetizados além do modo silencioso. Confira em **Configurações** se o som das notificações não está definido como silencioso e se o dispositivo de áudio padrão (`PipeWire`/`PulseAudio`) está ativo.

---

## 4. Erro `403: access_denied` ao conectar conta Google (OAuth2)

Esse erro ocorre quando o projeto no Google Cloud Console está em modo **Testando** (*Testing*) e o e-mail usado no login não foi adicionado à lista de testadores.

**Solução:**
1. Acesse [Público-alvo no Google Auth Platform](https://console.cloud.google.com/auth/audience).
2. Role até **Usuários de teste** (*Test users*) e clique em **+ Add users**.
3. Adicione exatamente o endereço Gmail/Google que você está usando no Custódia e salve.
4. Tente conectar novamente pelo aplicativo.

Veja o passo a passo ilustrado em [Configuração OAuth2](OAUTH_SETUP.md).

---

## 5. JSON OAuth2 rejeitado ao importar (`Web` vs `Desktop app`)

O Custódia aceita apenas credenciais do tipo **App para computador** (*Desktop app*, campo `"installed"` no JSON). Credenciais do tipo **Aplicativo da Web** (`"web"`) são rejeitadas por segurança.

**Solução:**
- No Google Cloud Console (**APIs e serviços → Credenciais → + Criar credenciais → ID do cliente OAuth**), selecione **App para computador** (*Desktop app*), baixe o novo `client_secret_*.json` e importe no Custódia.

---

## 6. Pré-visualização de PDF, vídeo, áudio ou Office não carrega miniaturas

O núcleo do app funciona sem dependências externas, mas a geração completa de miniaturas e pré-visualizações multimídia utiliza ferramentas padrão do sistema quando disponíveis.

**Instale os pacotes opcionais recomendados:**
- **Fedora / RHEL:**
  ```bash
  sudo dnf install poppler-utils ghostscript ffmpeg libreoffice ImageMagick
  ```
- **Pop!_OS / Ubuntu / Debian:**
  ```bash
  sudo apt install poppler-utils ghostscript ffmpeg libreoffice imagemagick
  ```

---

## 7. Instalação ou atualização do pacote `.rpm` falha

- **Conflito ou pacote corrompido:** valide o hash `SHA-256` comparando com o arquivo `SHA256SUMS.txt` da mesma release ou use o instalador oficial:
  ```bash
  ./install-custodia.sh --update
  ```
- **Instalação manual com `dnf`:**
  ```bash
  sudo dnf install -y ./Custodia-3.0.5-1.x86_64.rpm
  ```

---

## 8. API REST local (`127.0.0.1:5080`) não responde

1. Confirme que o Custódia está em execução (o servidor HTTP embarcado sobe junto com a aplicação).
2. Teste localmente em *loopback*:
   ```bash
   curl -s http://127.0.0.1:5080/api/v1/status
   ```
3. Verifique se outro processo local já está ocupando a porta `5080`:
   ```bash
   ss -tlnp | grep 5080
   ```
4. Lembre-se de que a API escuta exclusivamente em `127.0.0.1` (não responde por IP de rede externa nem `0.0.0.0`).

---

## 9. Coletando diagnóstico técnico para reportar um bug

Na aba **Sobre** do Custódia:
1. Selecione **Bug / Erro** no formulário de suporte.
2. Clique em **Ver dados** para inspecionar o pacote de diagnóstico sanitizado (sem tokens, senhas ou conteúdo de arquivos).
3. Use **Copiar texto** caso o log completo ultrapasse o limite de URL do navegador ao abrir a issue no GitHub.
