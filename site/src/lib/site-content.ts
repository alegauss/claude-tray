// The copy lives here and nowhere else. Every section component imports a value from this
// module and only renders it — so a claim is an array element a reviewer can check against
// the product, not a string welded into the markup that displays it. The composition
// (which section, in which order, and which screenshot) lives in the JSX; this file is the
// words.
//
// Fragments carrying inline code or emphasis are modelled as a small tagged run list
// (`Rich`) rather than raw HTML, so a section renders them without dangerouslySetInnerHTML
// and the Markdown twin generator has a structure to convert rather than markup to parse.

export type Run = string | { code: string } | { b: string } | { i: string };

export type Rich = Run[];

/* ------------------------------------------------------------------ meta + chrome */

export const meta = {
  title: "Claude Code Tray — Claude usage in your Windows tray",
  description:
    "A native Windows tray monitor for your Claude Code usage. A crisp DPI-aware icon, burn-rate projection, per-session cost at API list prices and a context audit — computed on your own machine, with no API key to paste.",
  og: {
    title: "Claude Code Tray",
    description:
      "Rate-limit %, burn-rate projection, per-session cost at list prices and a context audit — always in your Windows tray. Nothing leaves your machine.",
  },
} as const;

export const repoUrl = "https://github.com/alegauss/claude-tray";
export const parentUrl = "https://alegauss.github.io/";

// The release page rather than a file: the installer carries no version-independent URL of
// its own, so a hard-coded `ClaudeTray-Setup-1.2.0.exe` would 404 on the day 1.2.1 ships.
// `releases/latest` is the one link that cannot go stale.
export const releasesUrl = `${repoUrl}/releases/latest`;

export const wingetCommand = "winget install alegauss.ClaudeCodeTray";

// Section anchors (#x) act on the landing page; the page links are base-absolute so they
// resolve the same from every route. The brand and footer link home the same way.
export const navLinks = [
  { href: "/claude-tray/#projection", label: "Projection" },
  { href: "/claude-tray/#statistics", label: "Statistics" },
  { href: "/claude-tray/#context", label: "Context" },
  { href: "/claude-tray/#profiles", label: "Profiles" },
  { href: "/claude-tray/#features", label: "In depth" },
  // No "Download" link: it would sit immediately left of the download button and carry the
  // same destination twice. The button is the affordance.
] as const;

export const footer = {
  links: [
    { href: "/claude-tray/features/projection/", label: "Projection" },
    { href: "/claude-tray/features/statistics/", label: "Statistics" },
    { href: "/claude-tray/features/privacy/", label: "Privacy" },
    { href: repoUrl, label: "GitHub" },
    { href: releasesUrl, label: "Releases" },
    { href: `${repoUrl}/blob/main/README.md`, label: "Docs" },
    { href: `${repoUrl}/blob/main/LICENSE`, label: "License" },
  ],
  disclaimer:
    "Unofficial / community project — not affiliated with, endorsed by, or sponsored by Anthropic. “Claude” and “Claude Code” are trademarks of Anthropic; this tool merely reads the usage data Claude Code already exposes on your own machine. Apache License 2.0 © 2026 Alexandre Oliveira.",
} as const;

/* --------------------------------------------------------------- sponsor */

// Mirrors alegauss.github.io/sponsor.json — the canonical sponsor declaration for these
// projects. Transcribed here rather than fetched at runtime: this site is prerendered, and
// the whole point of naming a sponsor is that crawlers and LLMs read it in the served HTML.
export const sponsor = {
  label: "Sponsored by",
  name: "Viglet",
  url: "https://www.viglet.org",
  siteLabel: "viglet.org",
  logo: "/claude-tray/viglet/viglet-logo.png",
  summary:
    "Open source search and content tools for organisations with a lot to publish. Run on your own servers, with no per-user licence.",
  products: [
    {
      name: "Viglet Turing ES",
      url: "https://turing.viglet.org",
      logo: "/claude-tray/viglet/turing-logo.png",
      inline:
        "so visitors find what they came for, with AI answers drawn only from your own content",
    },
    {
      name: "Viglet Shio CMS",
      url: "https://shio.viglet.org",
      logo: "/claude-tray/viglet/shio-logo.png",
      inline:
        "so a new page goes live the same day, reviewed and approved by your own team",
    },
  ],
} as const;

/* ------------------------------------------------------------------ hero */

export const hero = {
  badge: "Unofficial · Open source · Windows 10 / 11",
  titleLead: "Your Claude Code usage,",
  titleAccent: "always in the tray.",
  sub: [
    "A native Windows tray monitor that draws your rate-limit usage as a crisp, DPI-aware icon — with burn-rate projection, per-session cost at API list prices and a context audit. Zero config: it reuses the login ",
    { code: "claude" },
    " already stored.",
  ] as Rich,
  // No emoji on these three, and that is a writing rule rather than a taste: an emoji glued
  // to the front of a feature line is the most recognisable mannerism of a generated landing
  // page, and these strings are also bullets in the Markdown twin an agent reads.
  meta: ["No API key, no setup", "Per-user install, no admin", "In-app updates"],
  pills: [
    [{ b: ".NET 10" }, " · WinForms + GDI+"] as Rich,
    ["Vector icon · ", { b: "PerMonitorV2 DPI" }] as Rich,
    ["Inno Setup ", { b: "installer" }] as Rich,
    [{ b: "Apache 2.0" }] as Rich,
  ],
};

/* ------------------------------------------------------------------ why */

export const why = {
  eyebrow: "Why you'll love it",
  heading: "Everything you need, nothing you don't",
  intro: [
    "Built for clarity on real displays — it lives entirely in the tray and tells you exactly where you stand.",
  ] as Rich,
  cards: [
    {
      icon: "🎯",
      title: "Crisp at any DPI",
      body: [
        "The percentage is drawn as a ",
        { b: "vector" },
        " at the exact size the tray requests — no downscaled bitmaps. It stays razor-sharp at 125–200%. Its colour says what the number is about: white for the ",
        { b: "5h session" },
        ", yellow for the ",
        { b: "7d week" },
        ", and ",
        { b: "orange whenever extra usage is paying" },
        " — the one thing on the tile that still says so when ",
        { i: "0 left" },
        " leaves no fill bar to.",
      ] as Rich,
    },
    {
      icon: "📊",
      title: "Task-Manager-style fill",
      body: [
        "A vertical bar rises from the bottom in proportion to your usage, with a 3D bevel for relief. Blue while you are on track, red when you are not.",
      ] as Rich,
    },
    {
      icon: "📈",
      title: "Smart projection",
      body: [
        "Projects when you would hit 100% — warning you ",
        { b: "before" },
        " the window resets, not after. A proportional pace line for the 7-day week, burn-rate regression for the 5h session.",
      ] as Rich,
    },
    {
      icon: "🔍",
      title: "24h usage insights",
      body: [
        "A local, cost-weighted breakdown of your last 24 hours — by model, by subagent, by large-context request. Computed offline from your own transcripts.",
      ] as Rich,
    },
    {
      icon: "⚡",
      title: "Zero configuration",
      body: [
        "No API key to paste, no environment variable to set. It reuses the OAuth token Claude Code already stores when you log in.",
      ] as Rich,
    },
    {
      icon: "🔄",
      title: "Self-updating",
      body: [
        "Checks GitHub Releases and offers a one-click in-app update. Per-user install, no administrator rights.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ tooltip */

export const tooltip = {
  eyebrow: "At a glance",
  heading: "One hover tells the whole story",
  intro: [
    "The tooltip packs everything into a single read — no dashboard, no browser tab.",
  ] as Rich,
  shot: {
    src: "/claude-tray/shots/tooltip.png",
    alt: "The tray tooltip: session and week usage, the projection, and what the rate limit says",
  },
  list: [
    [{ b: "5h session" }, " and ", { b: "7d week" }, " usage side by side"] as Rich,
    [
      { b: "Extra usage" },
      " and a live countdown to the next reset — plus ",
      { b: "how long you have been paying" },
      ", measured from the first reading past your included quota",
    ] as Rich,
    [
      "The ",
      { b: "projected time to 100%" },
      " at your current pace, labelled with the active window (e.g. ",
      { i: "Week 7d projection" },
      ")",
    ] as Rich,
    [
      "What the rate limit ",
      { b: "itself says" },
      " about that same window — its own status, named so the word is never about a window you are not watching",
    ] as Rich,
    [
      "Connection status — a clear ",
      { b: "not authenticated" },
      " prompt on token expiry, with one-click re-auth, and the logo while connecting",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ projection */

export const projection = {
  eyebrow: "Observability",
  heading: "It doesn't just show a number — it predicts",
  intro: [
    "The icon's colour tells you what is coming. The ",
    { b: "7-day week" },
    " uses a proportional ",
    { b: "pace line" },
    " — your usage against the share an even burn would have spent by now — which is accurate from the very first reading. The ",
    { b: "5h session" },
    " uses least-squares regression on a short rolling history of your utilization.",
  ] as Rich,
  states: [
    {
      key: "ok" as const,
      label: "On track",
      body: [
        "At your current pace, usage stays under 100% until the window resets. The fill bar stays its normal ",
        { b: "blue" },
        " — nothing to do.",
      ] as Rich,
    },
    {
      key: "bad" as const,
      label: "Danger",
      body: [
        "At your current pace, usage will hit 100% ",
        { b: "before" },
        " the reset. The fill bar turns ",
        { b: "vivid red" },
        " so you can ease off in time.",
      ] as Rich,
    },
    {
      key: "pay" as const,
      label: "Extra usage is paying",
      body: [
        "You are past the quota included in your plan and still working, because extra usage is enabled. The fill bar turns ",
        { b: "clay" },
        " rather than red — red means ",
        { b: "stopped" },
        ", clay means ",
        { b: "this is costing money" },
        ". What it shows you is how much of your ",
        { b: "extra-usage allowance" },
        " has gone: its own window, its own limit, its own reset, never mixed into the 0–100% of your included quota. And the number stops following your ",
        { b: "Show on icon" },
        " pick for as long as it lasts — a 5-hour session with room left cannot report quota the account no longer has.",
      ] as Rich,
    },
    {
      key: "bad" as const,
      label: "Blocked",
      body: [
        "A window is spent and ",
        { b: "nothing" },
        " is paying past it, so the account is blocked — and it stays blocked on the window that still has room. A week at 47% behind a spent session wears the red ",
        { b: "Blocked" },
        " chip and says so in words, where it used to read ",
        { i: "On track" },
        " over work that would not run. The tooltip deliberately says ",
        { b: "less" },
        ": no percentage and no window name, because naming ",
        { i: "Week 7d" },
        " there would caption the wrong number.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ statistics */

export const statistics = {
  eyebrow: "Consumption pace",
  heading: "See your burn, not just a number",
  intro: [
    "The ",
    { b: "Statistics" },
    " window draws each rate-limit window as a burn-up chart: your ",
    { b: "real utilization" },
    " against the even-pace line, with a dashed projection of where your current pace lands. Two tabs — the ",
    { b: "5-hour session" },
    " and the ",
    { b: "7-day week" },
    " — each with its own verdict, plus ",
    { b: "Throughput" },
    " for what is running right now and ",
    { b: "Sessions" },
    " for where it all went.",
  ] as Rich,
  shots: [
    {
      src: "/claude-tray/shots/statistics-5h.png",
      alt: "Statistics — 5-hour session burn-up chart, pacing ahead of the even-pace line",
      captionLead: "5-hour session",
      caption: "— burning ahead of pace, projected to hit 100% before the reset.",
    },
    {
      src: "/claude-tray/shots/statistics-7d.png",
      alt: "Statistics — 7-day week burn-up chart, the projection flat through the shaded overnight stretches",
      captionLead: "Week (7 days)",
      caption:
        "— the projection steps around your usual hours: flat overnight, running out mid-afternoon.",
    },
    {
      src: "/claude-tray/shots/statistics-overage.png",
      alt: "Statistics — the weekly chart past the included quota, with the clay extra-usage line on its own right-hand scale",
      captionLead: "Past the included quota",
      caption:
        "— the clay line is your extra-usage allowance on a right-hand scale of its own, never mixed into the 0–100%. When the API says you are over but never by how much, the chart shades the stretch and names no figure.",
    },
    {
      src: "/claude-tray/shots/statistics-throughput.png",
      alt: "Statistics — the Throughput tab: two three-minute line charts of the rolling rate, one line per project and one per token type",
      captionLead: "Throughput",
      caption:
        "— the last three minutes of the rolling rate: one line per repo, one per token type, each ceiling labelled in tok/s. Point at a second to read what landed in it.",
    },
    {
      src: "/claude-tray/shots/statistics-sessions.png",
      alt: "Statistics — the Sessions tab: one row per conversation with project, clock, duration, turns, tokens and cost at API list prices",
      captionLead: "Sessions",
      caption:
        "— where it went, one row per conversation instead of a total. A workflow's agents fold into the session that spawned them, so a fan-out is one row and not twelve. The last column reads ≈ $ at list prices, so a $2 conversation and a $40 one stop looking alike. It is not a bill.",
    },
  ],
  cards: [
    {
      icon: "🕘",
      title: "Paced to your hours",
      body: [
        "The weekly projection follows the hours you ",
        { b: "actually work" },
        " — flat through nights and weekends (shaded ",
        { i: "usually idle" },
        "), climbing while you are at the keyboard. So “you run out here” lands mid-afternoon, not at 4 a.m. It needs a few weeks of history; until then it is the plain average-pace line.",
      ] as Rich,
    },
    {
      icon: "💡",
      title: "And what to do about it",
      body: [
        { i: "“Stop now, pick it back up around Thursday 13:00, and you'd close the week at about 97%.”" },
        " The earliest hour you normally work that would still save the week — and if there is not one, no advice rather than invented advice.",
      ] as Rich,
    },
    {
      icon: "👻",
      title: "Last week, right behind it",
      body: [
        "Last week's burn-up is a faint line on the ",
        { b: "same axes" },
        ". ",
        { i: "“Worse than last week?”" },
        " becomes a glance instead of a memory test — no second chart, no extra number. A week too old to have recorded an overage says nothing rather than pretending it stayed inside.",
      ] as Rich,
    },
    {
      icon: "⏱️",
      title: "Two numbers, two clocks",
      body: [
        { b: "Window average" },
        " — what the window has cost so far. ",
        { b: "Now ≈ 870 tok/s" },
        " — what is running ",
        { i: "this minute" },
        ". Both are token ",
        { b: "throughput, not quota" },
        ".",
      ] as Rich,
    },
    {
      icon: "📉",
      title: "Live 3-minute charts",
      body: [
        "The ",
        { b: "rolling rate" },
        " sampled once a second, with the ceiling ruled and labelled in ",
        { b: "tok/s" },
        ". A line slides down through a pause because the work really is ageing out of the 60-second window. It scrolls because the axis is ",
        { b: "time" },
        " — the motion is the data, not a spinner. Idle goes flat, and it only repaints while the window is on screen.",
      ] as Rich,
    },
    {
      icon: "🗂️",
      title: "Across several repos",
      body: [
        "The upper chart is ",
        { b: "one line per repo" },
        " — the heaviest four, direct-labelled with their own rates, the rest as one grey “others”. Each keeps its colour while it is on screen, so nothing changes hands mid-read; the lower chart splits the same rate by token type. Because the useful question becomes ",
        { i: "where" },
        ", not ",
        { i: "how fast" },
        ".",
      ] as Rich,
    },
  ],
  note: [
    "All of it comes from your ",
    { b: "real logged utilization" },
    " and your local transcripts as they are written — no extra API calls. ",
    { b: "Nothing leaves your machine." },
  ] as Rich,
};

/* ------------------------------------------------------------------ notifications */

export const notifications = {
  eyebrow: "Reset alerts",
  heading: "When your quota comes back, you'll know — by colour",
  intro: [
    "Every reset gets a celebratory, on-brand toast: a confetti burst and the quota bar visibly refilling. Each kind has its ",
    { b: "own colour and headline" },
    ", so you can tell what happened the instant it appears — without reading a word.",
  ] as Rich,
  resets: [
    {
      tone: "rose" as const,
      src: "/claude-tray/shots/notify-surprise.png",
      alt: "Surprise! toast — weekly limit reset early",
      tagLead: "Surprise! · weekly reset ",
      tagBold: "early",
      body: "Your weekly limit reset ahead of its scheduled deadline — a known Claude Code quirk worth catching.",
    },
    {
      tone: "violet" as const,
      src: "/claude-tray/shots/notify-bonus.png",
      alt: "Bonus! toast — weekly usage credited back",
      tagLead: "Bonus! · usage ",
      tagBold: "credited back",
      body: "A partial mid-window credit dropped your weekly usage (91% → 50%). Found money.",
    },
    {
      tone: "teal" as const,
      src: "/claude-tray/shots/notify-weekly.png",
      alt: "New week! toast — routine weekly reset",
      tagLead: "New week! · ",
      tagBold: "weekly reset",
      body: "The calm, routine weekly reset — fresh quota for the week ahead.",
    },
    {
      tone: "blue" as const,
      src: "/claude-tray/shots/notify-session.png",
      alt: "Fresh session! toast — 5h session reset",
      tagLead: "Fresh session! · ",
      tagBold: "5h reset",
      body: "The 5-hour session window rolled over — fresh for the next five hours.",
    },
  ],
  plainEyebrow: "And three that aren't celebrations",
  plainHeading: "The same card, without the confetti",
  plainIntro: [
    "Not everything worth knowing is good news. These three use the same toast and say so plainly — one about what every session costs you before you type, one about the moment your quota stops being included, and one confirming a change you cannot otherwise see.",
  ] as Rich,
  plain: [
    {
      tone: "ochre" as const,
      src: "/claude-tray/shots/notify-context.png",
      alt: "Heavy startup context — a project loading a lot before the first prompt",
      tagLead: "Heavy startup ",
      tagBold: "context",
      body: "A project's instruction files, memory and skills have grown enough that every session pays for them before you type a word — with what it costs to load on a cold cache. At most once per project per week, and off by default: nobody asked to be told their own files are growing.",
    },
    {
      tone: "clay" as const,
      src: "/claude-tray/shots/notify-extra.png",
      alt: "Extra usage has started — past the quota included in your plan",
      tagLead: "Extra usage ",
      tagBold: "has started",
      body: "It exists because the four above do: the app interrupted you to say quota came back and said nothing when you started paying. Fires once when a check first finds you past your included quota — not on a timer, not again while the same spell lasts. A receipt, not a prompt.",
    },
    {
      tone: "slate" as const,
      src: "/claude-tray/shots/notify-profile.png",
      alt: "Profile set for Windows — the machine-wide profile switch landed",
      tagLead: "Profile ",
      tagBold: "set for Windows",
      body: "Writing the machine-wide profile is the least visible thing this app does — nothing on screen changes until you start another program. So it confirms itself: only when you switch by hand, once per switch, and only after the variable is read back. No quota bar, because animating one account's remaining quota into another's would suggest switching accounts because one ran out.",
    },
  ],
  note: [
    "All four resets are ",
    { b: "on by default" },
    " — toggle each one independently in ",
    { b: "Settings" },
    ".",
  ] as Rich,
};

/* ------------------------------------------------------------------ insights */

export const insights = {
  eyebrow: "Local · Private",
  heading: "Know where your tokens actually go",
  intro: [
    "The ",
    { b: "Usage insights (24h)" },
    " menu is computed entirely on your machine from Claude Code's session transcripts — weighted by per-model price, so the numbers reflect real cost rather than request count.",
  ] as Rich,
  shot: {
    src: "/claude-tray/shots/usage.png",
    alt: "The Usage insights (24h) submenu",
  },
  list: [
    [{ b: "Last 24h" }, " — request and session counts"] as Rich,
    [{ b: "From subagents" }, " — the share of usage from sidechain requests"] as Rich,
    [{ b: ">150k context" }, " — the share from large-context prompts"] as Rich,
    [
      { b: "By model" },
      " — the top models (Opus / Sonnet / Haiku / Fable) by share",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ context */

export const context = {
  eyebrow: "Local · Private",
  heading: "What every session costs before you type",
  intro: [
    "Claude Code loads your instruction files, your memory index and every skill's description before your first prompt — a toll paid on every request of the session, and normally invisible.",
  ] as Rich,
  shot: {
    src: "/claude-tray/shots/context.png",
    alt: "The Context Load window: the session-zero gauge, the per-source breakdown and the findings",
  },
  actHeading: "Then tells you what to do about it",
  actIntro: [
    "Every finding comes with one plain sentence and the concrete fix — and the only action is ",
    { b: "Copy cleanup prompt" },
    ", which hands paths and numbers to Claude Code. This app measures your files; it never edits them.",
  ] as Rich,
  actShot: {
    src: "/claude-tray/shots/context-all.png",
    alt: "All projects: the total footprint, the heaviest loads and duplicated memory directories",
  },
  list: [
    [
      { b: "Eager vs lazy" },
      " — a 300 KB memory directory can cost less than one bloated ",
      { code: "AGENTS.md" },
    ] as Rich,
    [
      { b: "Was it ever used?" },
      " — skills annotated ",
      { b: "45×" },
      " or ",
      { b: "never" },
      ", mined from your own transcripts",
    ] as Rich,
    [
      { b: "What-if" },
      " — tick rows and watch the gauge and the cost drop, before changing anything",
    ] as Rich,
    [
      { b: "Grade and drift" },
      " — A–F per project, and “+2.4 KB in the last 7 days”",
    ] as Rich,
    [
      { b: "Between projects" },
      " — duplicated memory directories and dead project directories, invisible from inside either",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ profiles */

export const profiles = {
  eyebrow: "System information",
  heading: "Which plan am I actually on?",
  intro: [
    "Settings has a page that answers it without you opening a single JSON file — and it is read-only, like everything else here.",
  ] as Rich,
  shots: [
    {
      src: "/claude-tray/shots/system.png",
      alt: "Settings, System information: the profile picker, the plan with its raw tier and seat, which credentials the profile uses, and a masked account holder",
      captionLead: "As it opens",
      caption:
        "— the profile being read, the plan, the credentials actually in use, and a holder you have to ask for.",
    },
    {
      src: "/claude-tray/shots/system-account.png",
      alt: "The Claude account card with the holder revealed: plan and seat, credentials in use, name and email, organization and role, extra usage",
      captionLead: "After Show",
      caption:
        "— name, email, organization and role. A sample account: an organization and its mail domain cannot be masked, so the app builds a fictional one for a screenshot.",
    },
    {
      src: "/claude-tray/shots/link-profiles.png",
      alt: "Settings, Claude Code: One setup across profiles — which profile keeps its files, and per folder whether it is merged, adopted, not offered or never linked",
      captionLead: "One setup across profiles",
      caption:
        "— which side keeps its files, and what sharing each folder would mean. Two sample accounts again; the plan is read off whichever pair you pick.",
    },
  ],
  cards: [
    {
      icon: "🎟️",
      title: "Your plan, by name",
      body: [
        "The rate-limit tier the API reports for your login, read as you would say it — ",
        { b: "Claude Max 5x" },
        " — with the raw tier, seat and billing type underneath. Plus whether extra usage is enabled, why the API is refusing it when it is, and how long your stored sign-in is valid.",
      ] as Rich,
    },
    {
      icon: "🪪",
      title: "Work and personal, one click apart",
      body: [
        "Claude Code keeps one account per configuration folder. Register them once and ",
        { b: "Open Claude Code" },
        " becomes a submenu — each profile launches with its own folder, its own login and its own working directory, so the work account cannot open in a personal repo. The tray never writes to a config folder and never moves credentials: it passes ",
        { code: "CLAUDE_CONFIG_DIR" },
        " and lets Claude Code do the rest.",
      ] as Rich,
    },
    {
      icon: "🖥️",
      title: "… or the whole machine follows",
      body: [
        "Turn on ",
        { b: "Use the chosen profile everywhere in Windows" },
        " and picking a profile writes ",
        { code: "CLAUDE_CONFIG_DIR" },
        " into your user environment — so a terminal you open yourself and an editor started from the Start menu use it too, not only what the tray launches. It follows the profile you ",
        { b: "choose" },
        ", never the one auto-follow drifts to. Off by default, and reversible.",
      ] as Rich,
    },
    {
      icon: "🧭",
      title: "The icon keeps up on its own",
      body: [
        "Turn on ",
        { b: "Follow the active profile" },
        " and the icon moves to whichever profile Claude Code last worked in — no click to switch. Read from transcript ",
        { b: "timestamps only" },
        ", on the refresh you already pay for. Picking a profile by hand ",
        { b: "pins" },
        " the icon there until you click ",
        { b: "Resume following" },
        ". Off by default — and it tells you when it ",
        { b: "cannot" },
        " do anything: two profiles reading one project history report the same last turn, which is the same fact twice, so the setting's own description says so instead of leaving you a switch that looks on and never moves the icon.",
      ] as Rich,
    },
    {
      icon: "🔗",
      title: "Or make the two one setup",
      body: [
        "Two profiles start genuinely empty of each other — right when they are two jobs, in the way when they are one person changing subscription. Pick which profile ",
        { b: "keeps its files" },
        " and the page shows, folder by folder, what sharing them would mean: ",
        { b: "merged" },
        " for your projects, session history and skills, with the number that would be copied over; ",
        { b: "adopted" },
        " whole for plugins and your ",
        { code: "CLAUDE.md" },
        "; ",
        { b: "not offered" },
        " for ",
        { code: "settings.json" },
        ", because a union would widen the other account's permission allowlist and that is your call — so the script shows you what you are deciding: how many rules arrive on each side, in which lists, and the ones landing in ",
        { code: "deny" },
        " counted apart, since those take capability away rather than granting it; and ",
        { b: "never" },
        " for your sign-in and the file that makes it a separate account. And it names everything else in the two folders that it has ",
        { b: "no opinion about" },
        " — because a list of opinions that says nothing about what is not on it reads as a complete one.",
      ] as Rich,
    },
    {
      icon: "📜",
      title: "It hands you a script, not a button",
      body: [
        "This app cannot write into a configuration folder and is not growing a way to, so it writes a ",
        { b: "PowerShell script" },
        " where you point it and opens the folder. Read it first. Even then a bare run only prints what it would do — it takes a second run with ",
        { code: "-Apply" },
        " to change anything. It never asks for administrator rights, and it never deletes: every original is renamed beside its link, so you can put it back. Its header also names the one consequence — both profiles then report the same last turn, so ",
        { b: "Follow the active profile" },
        " stops moving the icon between that pair — before you run it rather than weeks after.",
      ] as Rich,
    },
    {
      icon: "🫥",
      title: "Masked by default",
      body: [
        "Account holder, email and absolute paths are hidden until you ask for them, because this is the page that ends up in a screenshot. ",
        { b: "Show" },
        " reveals them, ",
        { b: "Hide" },
        " puts them back.",
      ] as Rich,
    },
    {
      icon: "📈",
      title: "Statistics, per account",
      body: [
        "The Statistics window reports on one profile at a time and says which. Switch it at the top and the charts, the projection, the weekly shape and the live throughput are all recomputed from that account's own readings and its own transcripts.",
      ] as Rich,
    },
    {
      icon: "🔑",
      title: "Is it even your subscription?",
      body: [
        "A folder you have never signed into runs on whatever ",
        { code: "ANTHROPIC_API_KEY" },
        " is in your environment — billed per use, and invisible to the 5-hour and weekly windows. The page says which credentials each profile really uses, and warns in plain words when the percentages are not describing what Claude Code is spending.",
      ] as Rich,
    },
    {
      icon: "📂",
      title: "Your install, at a glance",
      body: [
        "Installed CLI version, install method, auto-update state, how many projects Claude Code tracks, and the configuration folder — with a button to open it. ",
        { code: "CLAUDE_CONFIG_DIR" },
        " is honoured.",
      ] as Rich,
    },
    {
      icon: "📋",
      title: "Copy for a bug report",
      body: [
        "Windows build, .NET runtime, architecture and the tray's own version and data folder — the whole page as plain text in one click, exactly as shown, so a masked holder stays masked.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ privacy */

export const privacy = {
  icon: "🔒",
  heading: "Privacy by design",
  body: [
    [
      "Only token counts, model ids and flags are read — ",
      { b: "never your message content" },
      ", with one exception you can see: the ",
      { b: "Sessions" },
      " list names each conversation, using the title Claude Code generated for it or the prompt that opened it, because a list you cannot name is a list you cannot search. Both are capped at 200 characters before they are stored, they appear on that one screen, and nothing further into a conversation is ever read.",
    ] as Rich,
    [
      "The insights scan is bounded to files touched in the last 24 hours and runs in the background. The rate-limit reading is a single 1-token API call every five minutes. Nothing leaves your machine beyond that, and there is nothing to log into. Usage history is stored locally on disk and never uploaded.",
    ] as Rich,
    [
      "The context scan reads only sizes, names, timestamps and frontmatter from ",
      { code: "~/.claude" },
      " — ",
      { b: "never the contents of your memories, instructions or skills" },
      " — and it never edits them: the one action it offers is copying a cleanup prompt for Claude Code to act on.",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ install */

export const install = {
  eyebrow: "Get started",
  heading: "Up and running in two minutes",
  intro: ["Already using Claude Code? Then you're already set up."] as Rich,
  steps: [
    {
      title: "Have Claude Code",
      body: [
        "Install it and run ",
        { code: "claude" },
        " once, so it stores your login token.",
      ] as Rich,
    },
    {
      title: "Install it",
      body: [
        "One command with ",
        { b: "winget" },
        ", or grab ",
        { code: "ClaudeTray-Setup.exe" },
        " from Releases. Per-user, no admin.",
      ] as Rich,
    },
    {
      title: "It just works",
      body: [
        "The icon appears in your tray and starts reporting. Right-click for options.",
      ] as Rich,
    },
  ],
  wingetLead: ["Install from the ", { b: "Windows Package Manager" }, ":"] as Rich,
  wingetNote: ["Prefer a direct download? Grab the installer below."] as Rich,
  cta: "⬇ Download the latest release",
};

/* ------------------------------------------------------------------ menu */

export const menu = {
  eyebrow: "One window, right-click menu",
  heading: "Control, without leaving the tray",
  shot: {
    src: "/claude-tray/shots/menu.png",
    alt: "The tray's right-click menu",
  },
  list: [
    [
      { b: "Click the icon" },
      " — one window on the pacing report, with ",
      { b: "Statistics" },
      ", ",
      { b: "Context" },
      " and ",
      { b: "Settings" },
      " as tabs along the top: everything is navigated to, nothing opens a second window",
    ] as Rich,
    [
      { b: "Show on icon" },
      " — switch between Session 5h / Week 7d / Extra, remembered across restarts",
    ] as Rich,
    [
      { b: "Used or remaining" },
      " — flip the icon to count down your ",
      { i: "remaining" },
      " quota instead of counting up usage",
    ] as Rich,
    [
      { b: "Profile" },
      " — with more than one Claude Code profile: each one's usage, a check on the one the icon follows, click to switch",
    ] as Rich,
    [{ b: "Refresh now" }, " — an immediate API read"] as Rich,
    [
      { b: "Open Claude Code" },
      " — re-authenticate in one click if the token expires",
    ] as Rich,
    [
      { b: "Update to vX.Y.Z" },
      " — appears only when a newer release exists",
    ] as Rich,
  ],
};

/* ------------------------------------------------------------------ download */

export const download = {
  eyebrow: "Get it",
  heading: "Install it, and forget about it",
  cta: "⬇ Download for Windows",
  ctaShort: "⬇ Download",
  secondary: "See the release notes",
  intro: [
    "A per-user install with no administrator prompt: it lands under your own profile, adds itself to the tray, and reads the login ",
    { code: "claude" },
    " already stored. There is nothing to configure and nothing to sign into.",
  ] as Rich,
  facts: [
    "Windows 10 / 11 · x64",
    "No API key, no account",
    "In-app updates from GitHub Releases",
  ],
  note: [
    "Apache 2.0, and building from source is documented in the ",
    { code: "README" },
    ". The tray never writes to a Claude Code configuration folder — it passes ",
    { code: "CLAUDE_CONFIG_DIR" },
    " and reads what is already there.",
  ] as Rich,
};
