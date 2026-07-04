using System.Globalization;
using System.Windows.Markup;

namespace ClaudeTray;

/// <summary>
/// Lightweight, dependency-free localization for the whole app. The display language is picked once
/// at startup from the operating system's UI language (<see cref="CultureInfo.CurrentUICulture"/>) —
/// Portuguese for any <c>pt-*</c> culture, English for everything else — with English as the default
/// and the fallback for any key a language table is missing.
///
/// Strings are held in plain in-memory dictionaries (keyed by a stable identifier) rather than .resx
/// satellite assemblies, so they travel inside the single self-contained .exe with zero extra build
/// or publish steps. Code reads a string with <see cref="T(string)"/> / <see cref="T(string, object[])"/>;
/// XAML reads one with the <c>{local:Loc Key}</c> markup extension.
///
/// The language is fixed for the process lifetime (there is no runtime switch), which is why the XAML
/// extension can resolve at parse time. To add a language, add its table and extend
/// <see cref="Detect"/>.
/// </summary>
internal static class L
{
    internal enum Lang { En, PtBr }

    /// <summary>The active language, decided from the OS UI language at first access.</summary>
    public static Lang Current { get; } = Detect();

    private static Lang Detect()
    {
        try
        {
            // CurrentUICulture follows the Windows display language (GetUserDefaultUILanguage), which
            // is what "the OS default language" means to a user. Any Portuguese variant → pt-BR.
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("pt", StringComparison.OrdinalIgnoreCase))
                return Lang.PtBr;
        }
        catch { /* fall through to English */ }
        return Lang.En;
    }

    /// <summary>The localized string for <paramref name="key"/>, falling back to English then the key.</summary>
    public static string T(string key)
    {
        if (Current == Lang.PtBr && PtBr.TryGetValue(key, out string? pt))
            return pt;
        return En.TryGetValue(key, out string? en) ? en : key;
    }

    /// <summary>A localized format string filled with <paramref name="args"/> (see <see cref="T(string)"/>).</summary>
    public static string T(string key, params object[] args) => string.Format(T(key), args);

    // ===================================================================================
    // English — the source language and the fallback for any key missing from a translation.
    // Product names (Claude, Claude Code, Claude Code Tray, Anthropic), CLI tokens (/login,
    // claude), model ids and raw API status words are intentionally NOT localized.
    // ===================================================================================
    private static readonly Dictionary<string, string> En = new()
    {
        // ---- Tray menu ----
        ["menu.showOnIcon"] = "Show on icon",
        ["menu.metric.5h"] = "Session 5h",
        ["menu.metric.7d"] = "Week 7d",
        ["menu.metric.extra"] = "Extra",
        ["menu.insights"] = "Usage insights (24h)",
        ["menu.statistics"] = "Statistics",
        ["menu.refreshNow"] = "Refresh now",
        ["menu.openClaude"] = "Open Claude Code",
        ["menu.updateAvailable"] = "Update available",
        ["menu.updateTo"] = "Update to {0}",
        ["menu.downloading"] = "Downloading {0}…",
        ["menu.settings"] = "Settings…",
        ["menu.quit"] = "Quit",

        // ---- Usage insights submenu ----
        ["insights.loading"] = "…",
        ["insights.computing"] = "Computing…",
        ["insights.unavailable"] = "Unavailable: {0}",
        ["insights.none"] = "No usage in the last 24h",
        ["insights.summary"] = "Last 24h: {0} requests, {1} sessions",
        ["insights.subagents"] = "From subagents: {0}",
        ["insights.heavyContext"] = ">150k context: {0}",
        ["insights.byModel"] = "By model:",
        ["insights.recompute"] = "Recompute",
        ["insights.noTranscripts"] = "no transcripts found",

        // ---- Tray tooltip ----
        ["tip.connecting"] = "Claude Code — connecting…",
        ["tip.notSignedIn"] = "Claude Code — not signed in\nOpen Claude Code and run /login to sign in",
        ["tip.willAppear"] = "Once you start using Claude, your usage will appear here",
        ["tip.apiError"] = "Claude Code — API error\n{0}",
        ["tip.session"] = "Session 5h",
        ["tip.week"] = "Week 7d",
        ["tip.extra"] = "Extra",
        ["tip.leftSuffix"] = " left",
        ["tip.status"] = "Status: {0}{1}",
        ["tip.atLimitFull"] = "⚠ {0}: at limit ({1})",
        ["tip.atLimitCompact"] = "⚠ at limit ({0})",
        ["tip.dangerEtaFull"] = "⚠ {0} projection: {1} in {2} (before reset)",
        ["tip.dangerEtaCompact"] = "⚠ {0} in {1}",
        ["tip.dangerPaceFull"] = "⚠ {0} projection: above safe pace (before reset)",
        ["tip.dangerPaceCompact"] = "⚠ above safe pace",
        ["tip.okTrackFull"] = "✓ {0} projection: on track",
        ["tip.okTrackCompact"] = "✓ on track",
        ["tip.okEtaFull"] = "✓ {0} projection: {1} in {2} (after reset)",
        ["tip.okEtaCompact"] = "✓ {0} in {1}",
        ["tip.limitUsed"] = "100%",
        ["tip.limitLeft"] = "0% left",
        ["tip.hitsUsed"] = "100%",
        ["tip.hitsLeft"] = "runs out",

        // ---- Durations ----
        ["dur.now"] = "now",

        // ---- Update balloon ----
        ["update.title"] = "Claude Code Tray — update available",
        ["update.body"] = "Version {0} is available (you have {1}). Click to install.",

        // ---- Message boxes / dialogs ----
        ["dialog.appName"] = "Claude Code Tray",
        ["dialog.saveFailed"] = "Could not save settings:\n{0}",
        ["dialog.downloadFailed"] = "Could not download the update:\n{0}",
        ["dialog.launchFailed"] = "Could not launch Claude Code automatically.\nOpen a terminal, run \"claude\", type /login to sign in, then choose \"Refresh now\".\n\n{0}",
        ["dialog.openLinkFailed"] = "Could not open the link:\n{0}\n\n{1}",
        ["dialog.startupFailed"] = "Could not change the startup setting:\n{0}",

        // ---- Open Claude Code terminal hints ----
        ["cli.loginHint"] = "Type  /login  to sign in again, then you can close this window.",
        ["cli.refreshHint"] = "Refreshing your Claude Code session — usage will update shortly.",

        // ---- Reset toast (Program.ResetToastContent) ----
        ["toast.quotaLeft.weekly"] = "Weekly quota left",
        ["toast.quotaLeft.session"] = "Session quota left",
        ["toast.limitNoun.weekly"] = "weekly limit",
        ["toast.limitNoun.session"] = "5h session",
        ["toast.freshSuffix.weekly"] = "fresh quota for the week",
        ["toast.freshSuffix.session"] = "fresh for the next 5 hours",
        ["toast.resetTitle.weekly"] = "New week!",
        ["toast.resetTitle.session"] = "Fresh session!",
        ["toast.aheadDays"] = "{0} ahead of schedule",
        ["toast.ahead"] = "ahead of schedule",
        ["toast.caption.earlyLeft"] = "Had {0}% left · {1}",
        ["toast.caption.earlyUsed"] = "Was {0}% used · {1}",
        ["toast.caption.creditLeft"] = "Quota left rose {0}% → {1}%",
        ["toast.caption.creditUsed"] = "Usage dropped {0}% → {1}%",
        ["toast.caption.routineLeft"] = "Had {0}% left · {1}",
        ["toast.caption.routineUsed"] = "Was {0}% used · {1}",
        ["toast.title.surprise"] = "Surprise!",
        ["toast.title.bonus"] = "Bonus!",
        ["toast.sub.early"] = "Your {0} reset early",
        ["toast.sub.credit"] = "Some {0} usage was credited back",
        ["toast.sub.routine"] = "Your {0} just reset",
        ["toast.scope.weekly"] = "weekly",
        ["toast.scope.session"] = "session",

        // ---- ApiClient status/error ----
        ["api.tempUnavailable"] = "Anthropic is temporarily unavailable (HTTP {0}). Retrying…",
        ["api.signInHint"] = "Not signed in to Claude Code. Open Claude Code and run /login to sign in.",
        ["api.refreshHint"] = "Once you start using Claude, your usage will appear here.",
        ["api.timeout"] = "Couldn't reach Anthropic — the request timed out. Retrying…",
        ["api.networkError"] = "Couldn't reach Anthropic — network error. Retrying…",

        // ================= Settings window =================
        ["settings.title"] = "Settings",
        ["settings.nav.general"] = "General",
        ["settings.nav.display"] = "Display",
        ["settings.nav.claudeCode"] = "Claude Code",
        ["settings.nav.notifications"] = "Notifications",
        ["settings.nav.about"] = "About",
        ["settings.save"] = "Save",
        ["settings.cancel"] = "Cancel",
        ["settings.close"] = "Close",
        ["settings.version"] = "Version {0}",
        ["settings.heroVersion"] = "v{0}",

        // General page
        ["settings.general.title"] = "General",
        ["settings.general.subtitle"] = "Polling cadence and startup behaviour.",
        ["settings.general.refresh"] = "Refresh",
        ["settings.general.interval"] = "Interval",
        ["settings.general.intervalDesc"] = "How often the tray checks your Claude usage.",
        ["settings.general.startup"] = "Startup",
        ["settings.general.startWithWindows"] = "Start with Windows",
        ["settings.general.startWithWindowsDesc"] = "Launch Claude Code Tray automatically when you sign in.",
        ["settings.min.one"] = "1 minute",
        ["settings.min.many"] = "{0} minutes",
        ["settings.sec.one"] = "1 second",
        ["settings.sec.many"] = "{0} seconds",
        ["settings.cost.lead"] = "Each refresh is one 1-token Haiku call (“heartbeat”) sent with your Claude Code login — it uses a sliver of your usage, not a separate bill.\n",
        ["settings.cost.stats"] = "At this interval: ≈ {0} calls/h · ~{1} tokens/h (~{2}/day · ≈ ${3}/mo if billed as pay-as-you-go API).",

        // Display page
        ["settings.display.title"] = "Display",
        ["settings.display.subtitle"] = "How your usage reads on the tray icon and its tooltip.",
        ["settings.display.showPct"] = "Show percentage on the icon",
        ["settings.display.showPctDesc"] = "Draw the usage percentage number on the tray icon; otherwise just the fill bar.",
        ["settings.display.showRemaining"] = "Show remaining instead of used",
        ["settings.display.showRemainingDesc"] = "Count down the quota you have left: the icon starts full at 100% and drains to 0%, and the tooltip reads “… left”. The warning color and alerts are unchanged.",

        // Claude Code page
        ["settings.cc.title"] = "Claude Code",
        ["settings.cc.subtitle"] = "Where Claude Code launches from the tray, and how the tray behaves while you’re signed out.",
        ["settings.cc.workDir"] = "Working directory",
        ["settings.cc.workDirDesc"] = "Used when launching Claude Code from the tray menu or auto-open. Defaults to your home directory; clear it to reset.",
        ["settings.cc.browse"] = "Browse…",
        ["settings.cc.browseTitle"] = "Choose the directory Claude Code opens in",
        ["settings.cc.autoOpen"] = "Open automatically when signed out",
        ["settings.cc.autoOpenDesc"] = "Launch Claude Code when a check finds you signed out, so it can refresh the expired token.",
        ["settings.cc.retry"] = "Retry while signed out",
        ["settings.cc.retryDesc"] = "How often to re-check your usage after a sign-out, until you authenticate again.",

        // Notifications page
        ["settings.notif.title"] = "Notifications",
        ["settings.notif.subtitle"] = "Which usage events show a tray notification, and how much usage is worth a heads-up.",
        ["settings.notif.routine"] = "Routine resets",
        ["settings.notif.weekly"] = "Routine weekly reset",
        ["settings.notif.weeklyDesc"] = "A calmer “fresh week, quota's back” notification when your weekly limit resets on schedule.",
        ["settings.notif.weeklyMin"] = "Minimum weekly usage to notify",
        ["settings.notif.weeklyMinDesc"] = "Skip the weekly ping if the window was barely used before resetting.",
        ["settings.notif.session"] = "5-hour session reset",
        ["settings.notif.sessionDesc"] = "A “fresh session” notification when your 5-hour session window resets (can happen several times a day).",
        ["settings.notif.sessionMin"] = "Minimum session usage to notify",
        ["settings.notif.sessionMinDesc"] = "Skip the session ping if the window was barely used before resetting.",
        ["settings.notif.anomalies"] = "Anomalies",
        ["settings.notif.unexpected"] = "Unexpected usage drop",
        ["settings.notif.unexpectedDesc"] = "Shows a tray notification if weekly usage drops unexpectedly — resetting to 0% before its scheduled deadline, or a partial mid-window credit (e.g. 91% → 50%). The event is also saved to reset-events.log.",

        // About page
        ["settings.about.tagline"] = "A native Windows tray monitor for your Claude Code usage — at a glance, always on.",
        ["settings.about.platform"] = "Windows 10 · 11",
        ["settings.about.intro"] = "Claude Code Tray draws your rate-limit usage as a crisp, DPI-aware icon that lives only in the Windows tray — the session (5h), week (7d) and extra windows, with a burn-rate projection that warns before you run out. It's free, open source, and made by the community.",
        ["settings.about.links"] = "Project & links",
        ["settings.about.github"] = "GitHub repository",
        ["settings.about.githubDesc"] = "Source, README & docs",
        ["settings.about.releases"] = "Releases",
        ["settings.about.releasesDesc"] = "Downloads & changelog",
        ["settings.about.report"] = "Report an issue",
        ["settings.about.reportDesc"] = "Bugs & feature ideas",
        ["settings.about.star"] = "Star the project",
        ["settings.about.starDesc"] = "Open the repo and click ★",
        ["settings.about.involved"] = "Get involved",
        ["settings.about.involvedIntro"] = "This is an open-source community project — pull requests, bug reports and ideas are all welcome.",
        ["settings.about.involved1"] = "⭐  Star the repo to follow new releases",
        ["settings.about.involved2"] = "🐛  Open an issue for any bug or feature idea",
        ["settings.about.involved3"] = "🔧  Fork it, run \"dotnet build\", and send a pull request",
        ["settings.about.involved4"] = "📖  Read the README for the architecture notes",
        ["settings.about.disclaimer"] = "Unofficial community project — not affiliated with, endorsed by, or sponsored by Anthropic. \"Claude\" and \"Claude Code\" are trademarks of Anthropic. Released under the MIT License.",

        // ================= Statistics window =================
        ["stats.title"] = "Statistics",
        ["stats.header"] = "Consumption pace",
        ["stats.subtitle"] = "Are you spending your quota evenly, or faster than the clock — running out before it resets? The dashed line projects where your current pace lands.",
        ["stats.tab.session"] = "5-hour session",
        ["stats.tab.week"] = "Week (7 days)",
        ["stats.session.hint"] = "Short window — resets a few times a day",
        ["stats.week.hint"] = "Long window — the limit that defines your week",
        ["stats.stat.used"] = "of quota used",
        ["stats.stat.ideal"] = "even-pace target now",
        ["stats.stat.reset"] = "until reset",
        ["stats.legend.actual"] = "actual usage",
        ["stats.legend.evenPace"] = "even pace",
        ["stats.legend.projection"] = "projection",
        ["stats.methodNote"] = "“Used” and the reset come live from Anthropic's rate limits; the curve is your real measured utilization over the window (until enough is logged, it's reconstructed from local transcripts). The projection assumes the current average pace.",
        ["stats.refresh"] = "Refresh",
        ["stats.close"] = "Close",
        ["stats.updated"] = "Updated {0}",
        ["stats.connect"] = "Connect Claude Code to see your consumption pace. As soon as a usage reading comes in, the report appears here.",
        ["stats.computing"] = "Computing your consumption pace…",
        ["stats.buildFailed"] = "Couldn't build the report: {0}",

        // Verdict chips
        ["stats.verdict.onTrack"] = "On track",
        ["stats.verdict.tooFast"] = "Too fast",
        ["stats.verdict.atLimit"] = "At limit",
        ["stats.verdict.noData"] = "No data",

        // Throughput
        ["stats.tps.none"] = "No token activity logged for this window yet.",
        ["stats.tps.head"] = "Throughput ≈ {0} tok/s  ·  {1} tokens (window average, cache reads excluded)",
        ["stats.tps.input"] = "input",
        ["stats.tps.output"] = "output",
        ["stats.tps.cacheCreate"] = "cache-create",
        ["stats.tps.legend"] = "{0}  {1}/s",
        ["stats.tps.tip"] = "{0}: {1} tokens ({2} of throughput)",

        // Projection text
        ["stats.proj.noWindow"] = "No limit reading for this window yet.",
        ["stats.proj.atLimit"] = "You've hit this window's limit. It resets in {0} — until then, usage is blocked.",
        ["stats.proj.aheadEta"] = "At the current pace, your quota reaches 100% in {0} — about {1} before the reset (in {2}). Worth easing off so you don't get blocked.",
        ["stats.proj.ahead"] = "You're consuming above the even pace ({0} would be expected by now, but you've already used {1}). It resets in {2}.",
        ["stats.proj.ok"] = "You're pacing evenly — below the even-pace target of {0}. At the current rate, the quota comfortably lasts until the reset, in {1}.",

        // Chart
        ["stats.chart.noWindow"] = "No limit data for this window.",
        ["stats.chart.projHit"] = "Projected to hit 100% in {0}",
        ["stats.chart.projReset"] = "Projected at reset: {0}",
        ["stats.chart.idealNow"] = "Even-pace target now: {0}",
        ["stats.chart.currentUsage"] = "Current usage: {0}",
        ["stats.chart.start"] = "start {0}",
        ["stats.chart.reset"] = "reset {0}",
    };

    // ===================================================================================
    // Portuguese (Brazil). Any key omitted here falls back to the English entry above.
    // ===================================================================================
    private static readonly Dictionary<string, string> PtBr = new()
    {
        // ---- Tray menu ----
        ["menu.showOnIcon"] = "Mostrar no ícone",
        ["menu.metric.5h"] = "Sessão 5h",
        ["menu.metric.7d"] = "Semana 7d",
        ["menu.metric.extra"] = "Extra",
        ["menu.insights"] = "Análise de uso (24h)",
        ["menu.statistics"] = "Estatísticas",
        ["menu.refreshNow"] = "Atualizar agora",
        ["menu.openClaude"] = "Abrir Claude Code",
        ["menu.updateAvailable"] = "Atualização disponível",
        ["menu.updateTo"] = "Atualizar para {0}",
        ["menu.downloading"] = "Baixando {0}…",
        ["menu.settings"] = "Configurações…",
        ["menu.quit"] = "Sair",

        // ---- Usage insights submenu ----
        ["insights.loading"] = "…",
        ["insights.computing"] = "Calculando…",
        ["insights.unavailable"] = "Indisponível: {0}",
        ["insights.none"] = "Nenhum uso nas últimas 24h",
        ["insights.summary"] = "Últimas 24h: {0} requisições, {1} sessões",
        ["insights.subagents"] = "De subagentes: {0}",
        ["insights.heavyContext"] = ">150k de contexto: {0}",
        ["insights.byModel"] = "Por modelo:",
        ["insights.recompute"] = "Recalcular",
        ["insights.noTranscripts"] = "nenhuma transcrição encontrada",

        // ---- Tray tooltip ----
        ["tip.connecting"] = "Claude Code — conectando…",
        ["tip.notSignedIn"] = "Claude Code — não conectado\nAbra o Claude Code e execute /login para entrar",
        ["tip.willAppear"] = "Assim que você começar a usar o Claude, seu uso aparecerá aqui",
        ["tip.apiError"] = "Claude Code — erro de API\n{0}",
        ["tip.session"] = "Sessão 5h",
        ["tip.week"] = "Semana 7d",
        ["tip.extra"] = "Extra",
        ["tip.leftSuffix"] = " restante",
        ["tip.status"] = "Status: {0}{1}",
        ["tip.atLimitFull"] = "⚠ {0}: no limite ({1})",
        ["tip.atLimitCompact"] = "⚠ no limite ({0})",
        ["tip.dangerEtaFull"] = "⚠ projeção {0}: {1} em {2} (antes do reset)",
        ["tip.dangerEtaCompact"] = "⚠ {0} em {1}",
        ["tip.dangerPaceFull"] = "⚠ projeção {0}: acima do ritmo seguro (antes do reset)",
        ["tip.dangerPaceCompact"] = "⚠ acima do ritmo seguro",
        ["tip.okTrackFull"] = "✓ projeção {0}: dentro do ritmo",
        ["tip.okTrackCompact"] = "✓ dentro do ritmo",
        ["tip.okEtaFull"] = "✓ projeção {0}: {1} em {2} (após o reset)",
        ["tip.okEtaCompact"] = "✓ {0} em {1}",
        ["tip.limitUsed"] = "100%",
        ["tip.limitLeft"] = "0% restante",
        ["tip.hitsUsed"] = "100%",
        ["tip.hitsLeft"] = "esgota",

        // ---- Durations ----
        ["dur.now"] = "agora",

        // ---- Update balloon ----
        ["update.title"] = "Claude Code Tray — atualização disponível",
        ["update.body"] = "A versão {0} está disponível (você tem a {1}). Clique para instalar.",

        // ---- Message boxes / dialogs ----
        ["dialog.appName"] = "Claude Code Tray",
        ["dialog.saveFailed"] = "Não foi possível salvar as configurações:\n{0}",
        ["dialog.downloadFailed"] = "Não foi possível baixar a atualização:\n{0}",
        ["dialog.launchFailed"] = "Não foi possível iniciar o Claude Code automaticamente.\nAbra um terminal, execute \"claude\", digite /login para entrar e escolha \"Atualizar agora\".\n\n{0}",
        ["dialog.openLinkFailed"] = "Não foi possível abrir o link:\n{0}\n\n{1}",
        ["dialog.startupFailed"] = "Não foi possível alterar a opção de inicialização:\n{0}",

        // ---- Open Claude Code terminal hints ----
        ["cli.loginHint"] = "Digite  /login  para entrar novamente e depois pode fechar esta janela.",
        ["cli.refreshHint"] = "Atualizando sua sessão do Claude Code — o uso será atualizado em instantes.",

        // ---- Reset toast ----
        ["toast.quotaLeft.weekly"] = "Cota semanal restante",
        ["toast.quotaLeft.session"] = "Cota da sessão restante",
        ["toast.limitNoun.weekly"] = "limite semanal",
        ["toast.limitNoun.session"] = "sessão de 5h",
        ["toast.freshSuffix.weekly"] = "cota renovada para a semana",
        ["toast.freshSuffix.session"] = "renovada pelas próximas 5 horas",
        ["toast.resetTitle.weekly"] = "Nova semana!",
        ["toast.resetTitle.session"] = "Sessão renovada!",
        ["toast.aheadDays"] = "{0} adiantado",
        ["toast.ahead"] = "adiantado",
        ["toast.caption.earlyLeft"] = "Tinha {0}% restante · {1}",
        ["toast.caption.earlyUsed"] = "Estava {0}% usado · {1}",
        ["toast.caption.creditLeft"] = "Cota restante subiu {0}% → {1}%",
        ["toast.caption.creditUsed"] = "Uso caiu {0}% → {1}%",
        ["toast.caption.routineLeft"] = "Tinha {0}% restante · {1}",
        ["toast.caption.routineUsed"] = "Estava {0}% usado · {1}",
        ["toast.title.surprise"] = "Surpresa!",
        ["toast.title.bonus"] = "Bônus!",
        ["toast.sub.early"] = "Seu {0} resetou antes do previsto",
        ["toast.sub.credit"] = "Parte do uso {0} foi creditada de volta",
        ["toast.sub.routine"] = "Seu {0} acabou de resetar",
        ["toast.scope.weekly"] = "semanal",
        ["toast.scope.session"] = "da sessão",

        // ---- ApiClient status/error ----
        ["api.tempUnavailable"] = "A Anthropic está temporariamente indisponível (HTTP {0}). Tentando de novo…",
        ["api.signInHint"] = "Não conectado ao Claude Code. Abra o Claude Code e execute /login para entrar.",
        ["api.refreshHint"] = "Assim que você começar a usar o Claude, seu uso aparecerá aqui.",
        ["api.timeout"] = "Não foi possível acessar a Anthropic — a requisição expirou. Tentando de novo…",
        ["api.networkError"] = "Não foi possível acessar a Anthropic — erro de rede. Tentando de novo…",

        // ================= Settings window =================
        ["settings.title"] = "Configurações",
        ["settings.nav.general"] = "Geral",
        ["settings.nav.display"] = "Exibição",
        ["settings.nav.claudeCode"] = "Claude Code",
        ["settings.nav.notifications"] = "Notificações",
        ["settings.nav.about"] = "Sobre",
        ["settings.save"] = "Salvar",
        ["settings.cancel"] = "Cancelar",
        ["settings.close"] = "Fechar",
        ["settings.version"] = "Versão {0}",
        ["settings.heroVersion"] = "v{0}",

        // General page
        ["settings.general.title"] = "Geral",
        ["settings.general.subtitle"] = "Frequência de verificação e comportamento na inicialização.",
        ["settings.general.refresh"] = "Atualização",
        ["settings.general.interval"] = "Intervalo",
        ["settings.general.intervalDesc"] = "Com que frequência a bandeja verifica seu uso do Claude.",
        ["settings.general.startup"] = "Inicialização",
        ["settings.general.startWithWindows"] = "Iniciar com o Windows",
        ["settings.general.startWithWindowsDesc"] = "Iniciar o Claude Code Tray automaticamente quando você fizer login.",
        ["settings.min.one"] = "1 minuto",
        ["settings.min.many"] = "{0} minutos",
        ["settings.sec.one"] = "1 segundo",
        ["settings.sec.many"] = "{0} segundos",
        ["settings.cost.lead"] = "Cada atualização é uma chamada Haiku de 1 token (“heartbeat”) enviada com seu login do Claude Code — usa uma fração do seu uso, não uma cobrança separada.\n",
        ["settings.cost.stats"] = "Neste intervalo: ≈ {0} chamadas/h · ~{1} tokens/h (~{2}/dia · ≈ US$ {3}/mês se cobrado como API pré-paga).",

        // Display page
        ["settings.display.title"] = "Exibição",
        ["settings.display.subtitle"] = "Como seu uso aparece no ícone da bandeja e na dica de ferramenta.",
        ["settings.display.showPct"] = "Mostrar porcentagem no ícone",
        ["settings.display.showPctDesc"] = "Desenha o número da porcentagem de uso no ícone da bandeja; caso contrário, apenas a barra de preenchimento.",
        ["settings.display.showRemaining"] = "Mostrar restante em vez de usado",
        ["settings.display.showRemainingDesc"] = "Conta a cota que resta: o ícone começa cheio em 100% e esvazia até 0%, e a dica mostra “… restante”. A cor de aviso e os alertas não mudam.",

        // Claude Code page
        ["settings.cc.title"] = "Claude Code",
        ["settings.cc.subtitle"] = "De onde o Claude Code é iniciado pela bandeja e como a bandeja se comporta enquanto você está desconectado.",
        ["settings.cc.workDir"] = "Diretório de trabalho",
        ["settings.cc.workDirDesc"] = "Usado ao iniciar o Claude Code pelo menu da bandeja ou pela abertura automática. Padrão é seu diretório pessoal; limpe para redefinir.",
        ["settings.cc.browse"] = "Procurar…",
        ["settings.cc.browseTitle"] = "Escolha o diretório em que o Claude Code será aberto",
        ["settings.cc.autoOpen"] = "Abrir automaticamente quando desconectado",
        ["settings.cc.autoOpenDesc"] = "Inicia o Claude Code quando uma verificação detecta que você está desconectado, para que ele possa renovar o token expirado.",
        ["settings.cc.retry"] = "Repetir enquanto desconectado",
        ["settings.cc.retryDesc"] = "Com que frequência reverificar seu uso após uma desconexão, até você autenticar novamente.",

        // Notifications page
        ["settings.notif.title"] = "Notificações",
        ["settings.notif.subtitle"] = "Quais eventos de uso mostram uma notificação na bandeja e quanto uso vale um aviso.",
        ["settings.notif.routine"] = "Resets de rotina",
        ["settings.notif.weekly"] = "Reset semanal de rotina",
        ["settings.notif.weeklyDesc"] = "Uma notificação mais tranquila de “nova semana, a cota voltou” quando seu limite semanal reseta na hora prevista.",
        ["settings.notif.weeklyMin"] = "Uso semanal mínimo para notificar",
        ["settings.notif.weeklyMinDesc"] = "Pula o aviso semanal se a janela foi pouco usada antes de resetar.",
        ["settings.notif.session"] = "Reset da sessão de 5 horas",
        ["settings.notif.sessionDesc"] = "Uma notificação de “sessão renovada” quando sua janela de sessão de 5 horas reseta (pode ocorrer várias vezes ao dia).",
        ["settings.notif.sessionMin"] = "Uso da sessão mínimo para notificar",
        ["settings.notif.sessionMinDesc"] = "Pula o aviso da sessão se a janela foi pouco usada antes de resetar.",
        ["settings.notif.anomalies"] = "Anomalias",
        ["settings.notif.unexpected"] = "Queda inesperada de uso",
        ["settings.notif.unexpectedDesc"] = "Mostra uma notificação na bandeja se o uso semanal cair inesperadamente — resetando para 0% antes do prazo previsto, ou um crédito parcial no meio da janela (ex.: 91% → 50%). O evento também é salvo em reset-events.log.",

        // About page
        ["settings.about.tagline"] = "Um monitor nativo na bandeja do Windows para seu uso do Claude Code — num relance, sempre ativo.",
        ["settings.about.platform"] = "Windows 10 · 11",
        ["settings.about.intro"] = "O Claude Code Tray desenha seu uso do limite de taxa como um ícone nítido e adaptado ao DPI que vive apenas na bandeja do Windows — as janelas de sessão (5h), semana (7d) e extra, com uma projeção de ritmo que avisa antes de você esgotar. É gratuito, de código aberto e feito pela comunidade.",
        ["settings.about.links"] = "Projeto & links",
        ["settings.about.github"] = "Repositório no GitHub",
        ["settings.about.githubDesc"] = "Código-fonte, README & docs",
        ["settings.about.releases"] = "Versões",
        ["settings.about.releasesDesc"] = "Downloads & changelog",
        ["settings.about.report"] = "Relatar um problema",
        ["settings.about.reportDesc"] = "Bugs & ideias de recursos",
        ["settings.about.star"] = "Dar uma estrela ao projeto",
        ["settings.about.starDesc"] = "Abra o repositório e clique em ★",
        ["settings.about.involved"] = "Participe",
        ["settings.about.involvedIntro"] = "Este é um projeto de código aberto e comunitário — pull requests, relatos de bugs e ideias são todos bem-vindos.",
        ["settings.about.involved1"] = "⭐  Dê uma estrela ao repositório para acompanhar novas versões",
        ["settings.about.involved2"] = "🐛  Abra uma issue para qualquer bug ou ideia de recurso",
        ["settings.about.involved3"] = "🔧  Faça um fork, execute \"dotnet build\" e envie um pull request",
        ["settings.about.involved4"] = "📖  Leia o README para as notas de arquitetura",
        ["settings.about.disclaimer"] = "Projeto comunitário não oficial — não afiliado, endossado ou patrocinado pela Anthropic. \"Claude\" e \"Claude Code\" são marcas da Anthropic. Distribuído sob a Licença MIT.",

        // ================= Statistics window =================
        ["stats.title"] = "Estatísticas",
        ["stats.header"] = "Ritmo de consumo",
        ["stats.subtitle"] = "Você está gastando sua cota de forma equilibrada ou mais rápido que o relógio — esgotando antes do reset? A linha tracejada projeta onde seu ritmo atual chega.",
        ["stats.tab.session"] = "Sessão de 5 horas",
        ["stats.tab.week"] = "Semana (7 dias)",
        ["stats.session.hint"] = "Janela curta — reseta algumas vezes ao dia",
        ["stats.week.hint"] = "Janela longa — o limite que define sua semana",
        ["stats.stat.used"] = "da cota usada",
        ["stats.stat.ideal"] = "meta de ritmo equilibrado agora",
        ["stats.stat.reset"] = "até o reset",
        ["stats.legend.actual"] = "uso real",
        ["stats.legend.evenPace"] = "ritmo equilibrado",
        ["stats.legend.projection"] = "projeção",
        ["stats.methodNote"] = "“Usado” e o reset vêm ao vivo dos limites de taxa da Anthropic; a curva é sua utilização real medida ao longo da janela (até haver registro suficiente, é reconstruída a partir das transcrições locais). A projeção assume o ritmo médio atual.",
        ["stats.refresh"] = "Atualizar",
        ["stats.close"] = "Fechar",
        ["stats.updated"] = "Atualizado {0}",
        ["stats.connect"] = "Conecte o Claude Code para ver seu ritmo de consumo. Assim que chegar uma leitura de uso, o relatório aparece aqui.",
        ["stats.computing"] = "Calculando seu ritmo de consumo…",
        ["stats.buildFailed"] = "Não foi possível montar o relatório: {0}",

        // Verdict chips
        ["stats.verdict.onTrack"] = "No ritmo",
        ["stats.verdict.tooFast"] = "Rápido demais",
        ["stats.verdict.atLimit"] = "No limite",
        ["stats.verdict.noData"] = "Sem dados",

        // Throughput
        ["stats.tps.none"] = "Ainda não há atividade de tokens registrada para esta janela.",
        ["stats.tps.head"] = "Vazão ≈ {0} tok/s  ·  {1} tokens (média da janela, leituras de cache excluídas)",
        ["stats.tps.input"] = "entrada",
        ["stats.tps.output"] = "saída",
        ["stats.tps.cacheCreate"] = "criação-cache",
        ["stats.tps.legend"] = "{0}  {1}/s",
        ["stats.tps.tip"] = "{0}: {1} tokens ({2} da vazão)",

        // Projection text
        ["stats.proj.noWindow"] = "Ainda não há leitura de limite para esta janela.",
        ["stats.proj.atLimit"] = "Você atingiu o limite desta janela. Ela reseta em {0} — até lá, o uso está bloqueado.",
        ["stats.proj.aheadEta"] = "No ritmo atual, sua cota chega a 100% em {0} — cerca de {1} antes do reset (em {2}). Vale desacelerar para não ser bloqueado.",
        ["stats.proj.ahead"] = "Você está consumindo acima do ritmo equilibrado ({0} seria o esperado até agora, mas você já usou {1}). Ela reseta em {2}.",
        ["stats.proj.ok"] = "Você está no ritmo equilibrado — abaixo da meta de {0}. No ritmo atual, a cota dura tranquilamente até o reset, em {1}.",

        // Chart
        ["stats.chart.noWindow"] = "Sem dados de limite para esta janela.",
        ["stats.chart.projHit"] = "Projeção de atingir 100% em {0}",
        ["stats.chart.projReset"] = "Projeção no reset: {0}",
        ["stats.chart.idealNow"] = "Meta de ritmo equilibrado agora: {0}",
        ["stats.chart.currentUsage"] = "Uso atual: {0}",
        ["stats.chart.start"] = "início {0}",
        ["stats.chart.reset"] = "reset {0}",
    };
}

/// <summary>
/// XAML markup extension that resolves a localized string at parse time: <c>{local:Loc Key}</c>.
/// The language is fixed for the process lifetime, so resolving once at load is correct — there is
/// no runtime language switch to react to.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
internal sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    /// <summary>The lookup key (e.g. <c>settings.title</c>).</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
