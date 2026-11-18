# Microsoft Store listing — Usage Tray for Claude Code (T409, T412)

What Partner Center asks for when the package is submitted, kept beside the manifest so a submission and
the code it describes are reviewed together. Keep the wording in step with the README: the pitch, the
privacy facts and the unofficial disclaimer are the same claims.

**This file is the source; the listing CSV and the screenshots are generated from it.** The CSV import
refuses an image named by file, though its own type column says a relative path is allowed (measured
twice, the second time with the CSV byte-for-byte in the export's format and the images in the imported
folder), while a Partner Center URL passes, even one from another language. So screenshots go up through
the submission API or the page, and the CSV carries the rest:

1. `powershell -File build\store-shots.ps1` captures the window in each language over synthetic data
   (`--lang`, `--sample`) and composes each capture under its caption below, into
   `dist\store-shots\<language>\screenshot-1..6.png`. Then `powershell -File build\store-submit.ps1`
   (`-DryRun` first) swaps every language's screenshots and captions through the Store submission API
   and commits the submission — it needs a Microsoft Entra app with the Manager role in Partner Center
   (Account settings > User management > Microsoft Entra applications), whose tenant id, client id and
   key go in the user variables `STORE_TENANT_ID`, `STORE_CLIENT_ID` and `STORE_CLIENT_SECRET`. Without
   the API, upload each folder in **its own** language through the page.
2. Upload the logos and the hero art from `dist\store-art\` in **one** language.
3. Export the listings (Store listings > Export listings); the uploaded images come back as Partner
   Center URLs.
4. Run `powershell -File build\store-listing-csv.ps1 -Template <the exported .csv> -ImagesFrom <the
   language of step 2>` and import the CSV it writes to `dist\store-listing\`. It fills every language's
   text from this file, copies the logo and hero URLs to the other languages, and turns on the caption of
   each screenshot that language has.

Each language section below must keep its shape — four bold headers (short description, description,
features, captions) and two `**Label:**` lines (keywords, then copyright) — or the scripts stop and say
which section broke. The captions are also the screenshots' headlines, in the order `store-shots.ps1`
captures: week, 5-hour session, pace warning, sessions, throughput, profiles.

## Images

`build\build-msix.cmd` writes them to `dist\store-art\`, each named for its slot (`ClaudeTray.exe
--store-art <dir>` alone redraws them):

| Slot | File |
|---|---|
| Poster art 9:16 | `poster-9x16-1440x2160.png` |
| Box art 1:1 | `box-1x1-2160x2160.png` |
| Super hero art 16:9 (Windows and Xbox image) | `hero-16x9-3840x2160.png` — no text on purpose: the Store overlays the title |
| App tile icon 300x300 | `tile-300x300.png` |
| 1:1 150x150 | `tile-150x150.png` |
| 1:1 71x71 | `tile-71x71.png` |

The desktop screenshots are per language, from `build\store-shots.ps1` (step 1 above): 1920x1080, since
the Store refuses one under 1366x768.

## Once per submission (not per language)

**Privacy policy URL:** https://alegauss.com/claude-tray/features/privacy/

**Website:** https://alegauss.com/claude-tray/

**Support contact:** https://github.com/alegauss/claude-tray/issues

### Notes for certification

The app is a notification-area (system tray) utility: after launch it shows an icon, not a window. Click
the icon to open the main window; right-click it for the menu. Without Claude Code signed in on the test
machine the icon shows a "not authenticated" state, which is the expected behaviour — the app needs a
Claude Code sign-in to have anything to show.

It starts with Windows through the package's startup task, which the user can turn off in Settings >
Apps > Startup; the app's own Settings page links there. The `ClaudeTray.exe` execution alias exists so
Claude Code's status-line command can run the app from any shell.

Data: the app sends no telemetry, analytics or crash reports, and nothing to the developer. To read
usage it makes one minimal request to the Anthropic API on each poll (every 5 minutes by default, less
when idle), using the OAuth sign-in Claude Code already stores in
`%USERPROFILE%\.claude\.credentials.json`, and reads only the rate-limit headers of the reply. From Claude
Code's local transcripts it reads token counts, model ids, flags, tool and skill names and the working
directory — never message content. Everything it stores stays in its own app data folder.

### Restricted capability: runFullTrust

Partner Center asks why the package declares `runFullTrust` (Packages > Restricted capabilities). Every
Win32 app packaged as MSIX declares it; the answer is what the app does with it:

> Usage Tray for Claude Code is a classic Win32 desktop application (.NET, WinForms and WPF) packaged as MSIX with
> the Desktop Bridge, so it runs as Windows.FullTrustApplication. runFullTrust is what lets it work as the
> desktop app it is; it does not elevate, install services or drivers, and runs entirely as the signed-in
> user.
>
> It needs full trust for these features:
> 1. A notification-area (system tray) icon that shows the user's Claude Code usage, drawn with
>    Shell_NotifyIcon and updated every few minutes.
> 2. Reading the files Claude Code keeps in the user's profile (%USERPROFILE%\.claude): its sign-in, used
>    only to read the user's own usage limits, and its session transcripts, from which only token counts,
>    model ids and similar metadata are read, never message content. These files are outside the app's
>    package and app data folders.
> 3. When the user turns it on in Settings, writing the user-scope CLAUDE_CONFIG_DIR environment variable
>    so that new terminals start Claude Code with the profile the user picked.
> 4. Launching Claude Code (the "claude" command-line tool) in a terminal from the tray menu, in the folder
>    the user chose.
> 5. Being run by Claude Code's status-line command through the ClaudeTray.exe execution alias, which
>    passes the current usage to the app on standard input.
>
> No data is sent to the developer; the only network request is a minimal call to the Anthropic API with
> the user's existing Claude Code sign-in, to read the rate-limit headers that show the user's own usage.

### Reusing the Claude Code sign-in (the policy question)

The app does not ask for, store or transmit credentials of its own. It reads the OAuth token Claude Code
already keeps on the user's machine, only to make the same minimal request Claude Code itself makes, so
it can show the user their own limits. The README sets out how this sits with Anthropic's terms
(section "Authentication & Anthropic's terms"). Whether that is acceptable for the Store is the owner's
answer to give, not this file's.

---

## English (en-us)

**Short description**

Your Claude Code limit in the Windows tray: red before you run out, and a weekly forecast paced to the
hours you actually work.

**Description**

Usage Tray for Claude Code shows how much of your Claude Code 5-hour session and weekly limit you have used, as a
crisp icon in the Windows notification area.

When your current pace would use the limit up before it resets, the icon turns red and a notification
says when — while there is still time to change course. The weekly forecast is paced to the hours you
actually work, not a straight line through nights and weekends.

It also shows per-session cost at API list prices, usage statistics, and a context audit of what Claude
Code loads before your first prompt — all computed on your own computer. It reads the sign-in Claude
Code already keeps here, so there is no API key to paste and no setup.

This is an unofficial, community-built app. It is not affiliated with, endorsed by, or sponsored by
Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic, PBC.

**Product features**

- Your 5-hour and weekly Claude Code usage as a sharp tray icon
- Turns red before you run out, with a notification while there is time
- Weekly forecast paced to the hours you actually work
- Per-session cost at API list prices
- Context audit: what Claude Code loads before your first prompt
- Several Claude Code profiles, one click apart
- No API key, no setup, no telemetry

**Keywords:** Claude Code, usage, rate limit, tray, quota, forecast, developer tools

**Copyright and trademark:** © Alegauss. "Claude" and "Claude Code" are trademarks of Anthropic, PBC.

**Screenshot captions**

1. The weekly forecast, paced to the hours you actually work
2. The 5-hour session and where your pace takes it
3. A warning while there is still time to change course
4. Every session, with its cost at API list prices
5. Live throughput, project by project and token by token
6. Two profiles, one setup: history, skills and plugins shared

## Português (Brasil) (pt-br)

**Descrição curta**

Seu limite do Claude Code na bandeja do Windows: fica vermelho antes de acabar, e uma previsão semanal no
ritmo das horas em que você de fato trabalha.

**Descrição**

O Usage Tray for Claude Code mostra quanto você já usou da sessão de 5 horas e do limite semanal do Claude Code,
num ícone nítido na área de notificação do Windows.

Quando o seu ritmo atual esgotaria o limite antes do reset, o ícone fica vermelho e uma notificação diz
quando — enquanto ainda dá tempo de mudar o rumo. A previsão semanal segue as horas em que você de fato
trabalha, e não uma linha reta que atravessa noites e fins de semana.

Também mostra o custo por sessão a preços de lista da API, estatísticas de uso e uma auditoria do
contexto que o Claude Code carrega antes do seu primeiro prompt — tudo calculado no seu próprio
computador. Ele usa o login que o Claude Code já guarda aqui: sem chave de API para colar e sem
configuração.

Este é um app não oficial, feito pela comunidade. Não é afiliado, endossado nem patrocinado pela
Anthropic. "Claude" e "Claude Code" são marcas registradas da Anthropic, PBC.

**Recursos do produto**

- Seu uso de 5 horas e semanal do Claude Code num ícone nítido na bandeja
- Fica vermelho antes de acabar, com uma notificação enquanto há tempo
- Previsão semanal no ritmo das horas em que você trabalha
- Custo por sessão a preços de lista da API
- Auditoria de contexto: o que o Claude Code carrega antes do primeiro prompt
- Vários perfis do Claude Code, a um clique de distância
- Sem chave de API, sem configuração, sem telemetria

**Palavras-chave:** Claude Code, uso, limite, bandeja, cota, previsão, ferramentas de desenvolvimento

**Direitos autorais e marcas:** © Alegauss. "Claude" e "Claude Code" são marcas registradas da Anthropic, PBC.

**Legendas das capturas de tela**

1. A previsão semanal, no ritmo das horas em que você trabalha
2. A sessão de 5 horas e onde o seu ritmo vai levá-la
3. Um aviso enquanto ainda dá tempo de mudar o rumo
4. Cada sessão, com o custo a preços de lista da API
5. Vazão ao vivo, por projeto e por tipo de token
6. Dois perfis, uma configuração: histórico, skills e plugins compartilhados

## Português (Portugal) (pt-pt)

**Descrição breve**

O seu limite do Claude Code na área de notificação do Windows: fica vermelho antes de acabar, e uma
previsão semanal ao ritmo das horas em que realmente trabalha.

**Descrição**

O Usage Tray for Claude Code mostra quanto já usou da sessão de 5 horas e do limite semanal do Claude Code, num
ícone nítido na área de notificação do Windows.

Quando o seu ritmo atual esgotaria o limite antes de repor, o ícone fica vermelho e uma notificação diz
quando — enquanto ainda há tempo para mudar de rumo. A previsão semanal segue as horas em que realmente
trabalha, e não uma linha reta que atravessa noites e fins de semana.

Mostra também o custo por sessão a preços de tabela da API, estatísticas de utilização e uma auditoria
do contexto que o Claude Code carrega antes do seu primeiro prompt — tudo calculado no seu próprio
computador. Usa a sessão que o Claude Code já guarda aqui: sem chave de API para colar e sem
configuração.

Esta é uma aplicação não oficial, feita pela comunidade. Não é afiliada, apoiada nem patrocinada pela
Anthropic. "Claude" e "Claude Code" são marcas registadas da Anthropic, PBC.

**Funcionalidades do produto**

- A sua utilização de 5 horas e semanal do Claude Code num ícone nítido
- Fica vermelho antes de acabar, com uma notificação enquanto há tempo
- Previsão semanal ao ritmo das horas em que trabalha
- Custo por sessão a preços de tabela da API
- Auditoria de contexto: o que o Claude Code carrega antes do primeiro prompt
- Vários perfis do Claude Code, à distância de um clique
- Sem chave de API, sem configuração, sem telemetria

**Palavras-chave:** Claude Code, utilização, limite, área de notificação, quota, previsão, ferramentas de programação

**Direitos de autor e marcas:** © Alegauss. "Claude" e "Claude Code" são marcas registadas da Anthropic, PBC.

**Legendas das capturas de ecrã**

1. A previsão semanal, ao ritmo das horas em que trabalha
2. A sessão de 5 horas e onde o seu ritmo a leva
3. Um aviso enquanto ainda há tempo para mudar de rumo
4. Cada sessão, com o custo a preços de tabela da API
5. Débito ao vivo, por projeto e por tipo de token
6. Dois perfis, uma configuração: histórico, skills e plugins partilhados

## Français (fr-fr)

**Description courte**

Votre limite Claude Code dans la zone de notification de Windows : rouge avant d'être à court, et une
prévision hebdomadaire calée sur les heures où vous travaillez vraiment.

**Description**

Usage Tray for Claude Code montre ce que vous avez déjà utilisé de la session de 5 heures et de la limite
hebdomadaire de Claude Code, sous la forme d'une icône nette dans la zone de notification de Windows.

Quand votre rythme actuel épuiserait la limite avant sa réinitialisation, l'icône passe au rouge et une
notification indique quand — tant qu'il est encore temps de changer de cap. La prévision hebdomadaire
suit les heures où vous travaillez vraiment, et non une ligne droite qui traverse les nuits et les
week-ends.

Il affiche aussi le coût par session aux tarifs publics de l'API, des statistiques d'usage et un audit
du contexte que Claude Code charge avant votre premier prompt — le tout calculé sur votre propre
ordinateur. Il utilise la connexion que Claude Code conserve déjà ici : pas de clé d'API à coller, aucune
configuration.

Cette application est non officielle et développée par la communauté. Elle n'est ni affiliée, ni
approuvée, ni sponsorisée par Anthropic. « Claude » et « Claude Code » sont des marques d'Anthropic, PBC.

**Fonctionnalités du produit**

- Votre usage Claude Code sur 5 heures et sur la semaine, dans une icône nette
- Passe au rouge avant d'être à court, avec une notification tant qu'il est temps
- Prévision hebdomadaire calée sur vos heures de travail
- Coût par session aux tarifs publics de l'API
- Audit du contexte : ce que Claude Code charge avant le premier prompt
- Plusieurs profils Claude Code, à un clic l'un de l'autre
- Pas de clé d'API, aucune configuration, aucune télémétrie

**Mots-clés :** Claude Code, usage, limite, zone de notification, quota, prévision, outils de développement

**Droits d'auteur et marques:** © Alegauss. « Claude » et « Claude Code » sont des marques d'Anthropic, PBC.

**Légendes des captures d'écran**

1. La prévision hebdomadaire, calée sur vos heures de travail
2. La session de 5 heures, et où votre rythme la mène
3. Une alerte tant qu'il est temps de changer de cap
4. Chaque session, avec son coût aux tarifs publics de l'API
5. Le débit en direct, par projet et par type de token
6. Deux profils, une configuration : historique, skills et plugins partagés

## Español (es-es)

**Descripción breve**

Tu límite de Claude Code en la bandeja de Windows: en rojo antes de que se agote, y una previsión semanal
al ritmo de las horas en que realmente trabajas.

**Descripción**

Usage Tray for Claude Code muestra cuánto has usado de la sesión de 5 horas y del límite semanal de Claude Code,
con un icono nítido en el área de notificación de Windows.

Cuando tu ritmo actual agotaría el límite antes del reinicio, el icono se pone en rojo y una
notificación indica cuándo, mientras aún hay tiempo de cambiar el rumbo. La previsión semanal sigue las
horas en que realmente trabajas, no una línea recta que atraviesa noches y fines de semana.

También muestra el coste por sesión a precios de lista de la API, estadísticas de uso y una auditoría
del contexto que Claude Code carga antes de tu primer prompt, todo calculado en tu propio ordenador.
Usa el inicio de sesión que Claude Code ya guarda aquí: sin clave de API que pegar y sin configuración.

Esta es una aplicación no oficial, creada por la comunidad. No está afiliada, respaldada ni patrocinada
por Anthropic. «Claude» y «Claude Code» son marcas comerciales de Anthropic, PBC.

**Características del producto**

- Tu uso de 5 horas y semanal de Claude Code en un icono nítido
- Se pone en rojo antes de agotarse, con un aviso mientras hay tiempo
- Previsión semanal al ritmo de las horas en que trabajas
- Coste por sesión a precios de lista de la API
- Auditoría de contexto: lo que Claude Code carga antes del primer prompt
- Varios perfiles de Claude Code, a un clic de distancia
- Sin clave de API, sin configuración, sin telemetría

**Palabras clave:** Claude Code, uso, límite, bandeja, cuota, previsión, herramientas de desarrollo

**Derechos de autor y marcas:** © Alegauss. «Claude» y «Claude Code» son marcas comerciales de Anthropic, PBC.

**Leyendas de las capturas de pantalla**

1. La previsión semanal, al ritmo de las horas en que trabajas
2. La sesión de 5 horas y adónde la lleva tu ritmo
3. Un aviso mientras aún hay tiempo de cambiar el rumbo
4. Cada sesión, con su coste a precios de lista de la API
5. El rendimiento en vivo, por proyecto y por tipo de token
6. Dos perfiles, una configuración: historial, skills y plugins compartidos

## Deutsch (de-de)

**Kurzbeschreibung**

Ihr Claude-Code-Limit im Windows-Infobereich: rot, bevor es ausgeht, und eine Wochenprognose nach den
Stunden, in denen Sie wirklich arbeiten.

**Beschreibung**

Usage Tray for Claude Code zeigt, wie viel Sie von der 5-Stunden-Sitzung und dem Wochenlimit von Claude Code
verbraucht haben, als scharfes Symbol im Infobereich von Windows.

Wenn Ihr aktuelles Tempo das Limit vor dem Reset aufbrauchen würde, wird das Symbol rot und eine
Benachrichtigung sagt, wann – solange noch Zeit ist, gegenzusteuern. Die Wochenprognose folgt den
Stunden, in denen Sie wirklich arbeiten, statt einer geraden Linie durch Nächte und Wochenenden.

Dazu kommen die Kosten pro Sitzung zu API-Listenpreisen, Nutzungsstatistiken und eine Kontextprüfung
dessen, was Claude Code vor Ihrem ersten Prompt lädt – alles auf Ihrem eigenen Computer berechnet. Die
App nutzt die Anmeldung, die Claude Code hier bereits speichert: kein API-Schlüssel, keine Einrichtung.

Dies ist eine inoffizielle, von der Community entwickelte App. Sie ist nicht mit Anthropic verbunden
und wird von Anthropic weder unterstützt noch gesponsert. „Claude“ und „Claude Code“ sind Marken von
Anthropic, PBC.

**Produktmerkmale**

- Ihre 5-Stunden- und Wochennutzung von Claude Code als scharfes Symbol
- Wird rot, bevor das Kontingent ausgeht, mit Hinweis, solange Zeit ist
- Wochenprognose nach den Stunden, in denen Sie wirklich arbeiten
- Kosten pro Sitzung zu API-Listenpreisen
- Kontextprüfung: was Claude Code vor dem ersten Prompt lädt
- Mehrere Claude-Code-Profile, einen Klick voneinander entfernt
- Kein API-Schlüssel, keine Einrichtung, keine Telemetrie

**Suchbegriffe:** Claude Code, Nutzungslimit, Kontingent, Ratenlimit, Infobereich, Prognose, Entwicklertools

**Urheberrecht und Marken:** © Alegauss. „Claude“ und „Claude Code“ sind Marken von Anthropic, PBC.

**Bildunterschriften der Screenshots**

1. Die Wochenprognose, nach den Stunden, in denen Sie arbeiten
2. Die 5-Stunden-Sitzung und wohin Ihr Tempo sie führt
3. Eine Warnung, solange noch Zeit zum Gegensteuern ist
4. Jede Sitzung, mit ihren Kosten zu API-Listenpreisen
5. Live-Durchsatz, nach Projekt und nach Tokenart
6. Zwei Profile, eine Einrichtung: Verlauf, Skills und Plugins geteilt

## 中文（简体） (zh-cn)

**简短说明**

在 Windows 托盘中显示您的 Claude Code 限额：在用尽之前变红，并按您实际工作的时段预测本周用量。

**说明**

Usage Tray for Claude Code 以 Windows 通知区域中的清晰图标，显示您已用掉多少 Claude Code 5 小时会话和每周限额。

当按当前速度会在重置前用完限额时，图标会变红，并通过通知告诉您何时用完 — 趁还来得及调整。每周预测按您实际工作的时段计算，而不是一条贯穿夜晚和周末的直线。

它还按 API 标价显示每个会话的费用、使用统计，以及对 Claude Code 在您第一个提示前加载内容的上下文审查 — 全部在您自己的电脑上计算。它使用 Claude Code 已保存在本机的登录信息，无需粘贴 API 密钥，也无需任何设置。

这是一款非官方的社区应用。它与 Anthropic 无关联，亦未获 Anthropic 认可或赞助。“Claude”和“Claude Code”是 Anthropic, PBC 的商标。

**产品功能**

- 以清晰的托盘图标显示 Claude Code 5 小时和每周用量
- 在用尽之前变红，并趁还有时间发出通知
- 按您实际工作的时段预测本周用量
- 按 API 标价计算每个会话的费用
- 上下文审查：Claude Code 在第一个提示前加载了什么
- 多个 Claude Code 配置文件，一键切换
- 无需 API 密钥，无需设置，无遥测

**关键字:** Claude Code, 用量, 限额, 配额, 托盘, 预测, 开发者工具

**版权和商标:** © Alegauss。“Claude”和“Claude Code”是 Anthropic, PBC 的商标。

**屏幕截图说明**

1. 按您实际工作时段计算的每周预测
2. 5 小时会话，以及您的速度会把它带到哪里
3. 趁还来得及调整时发出的提醒
4. 每个会话，以及按 API 标价计算的费用
5. 实时吞吐量，按项目和 token 类型划分
6. 两个配置文件，一套设置：共享历史、技能和插件
