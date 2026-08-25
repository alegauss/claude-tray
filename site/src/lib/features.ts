import type { Rich } from "./site-content";

// The five depth pages, one record each. The route, the title and the description are all
// read off the same record (in routes.tsx), so a new pillar cannot ship half-declared or
// untitled: add a record here and its route, its <head> and its page all appear together,
// or none of them do.
//
// The copy here is the landing page's claim taken one level further — a depth page is where
// a reader goes to check the summary, so the two disagreeing is worse than either being
// stale alone.

export interface FeatureSection {
  heading: string;
  body?: Rich;
  list?: Rich[];
}

export interface FeatureShot {
  src: string;
  alt: string;
  caption?: string;
}

export interface FeatureRecord {
  slug: string;
  title: string;
  description: string;
  ogTitle: string;
  ogDescription: string;
  eyebrow: string;
  heading: string;
  lead: Rich;
  /** the anchor on the landing page this page expands */
  anchor: string;
  shot?: FeatureShot;
  sections: FeatureSection[];
}

export const features: FeatureRecord[] = [
  {
    slug: "projection",
    title: "Projection: the icon tells you before the window resets",
    description:
      "A proportional pace line for the 7-day week and least-squares regression for the 5h session, so the tray warns you before you hit 100% rather than after — and says clay when extra usage is paying, red only when work has stopped.",
    ogTitle: "Claude Code Tray: the projection",
    ogDescription:
      "Two windows, two models, four states — and why clay is not red.",
    eyebrow: "Observability",
    heading: "The projection",
    anchor: "projection",
    shot: {
      src: "/claude-tray/shots/tooltip.png",
      alt: "The tray tooltip: session and week usage, the projection, and what the rate limit says",
      caption:
        "One hover: both windows, the projection labelled with the window it belongs to, and what the rate limit itself says.",
    },
    lead: [
      "A percentage tells you where you are. It does not tell you whether you are going to make it. The tray runs two different forecasts, because the two rate-limit windows behave differently — and the icon's colour is the answer, so you get it without opening anything.",
    ],
    sections: [
      {
        heading: "Two windows, two models",
        list: [
          [
            "The ",
            { b: "7-day week" },
            " uses a proportional ",
            { b: "pace line" },
            ": your usage against the share an even burn would have spent by now. It is accurate from the very first reading, because it needs no history — only the clock.",
          ],
          [
            "The ",
            { b: "5h session" },
            " uses least-squares regression on a short rolling history of your utilization. A five-hour window is too short for an even-pace assumption to mean anything: what matters is the slope of the last few readings.",
          ],
          [
            "The weekly projection then ",
            { b: "steps around the hours you actually work" },
            " — flat overnight and at weekends, climbing while you are at the keyboard — so “you run out here” lands mid-afternoon rather than at 4 a.m. It needs a few weeks of history; until then it is the plain average-pace line.",
          ],
        ],
      },
      {
        heading: "Four states, and clay is not red",
        list: [
          [
            { b: "On track" },
            " — at your current pace, usage stays under 100% until the window resets. The fill bar stays blue.",
          ],
          [
            { b: "Danger" },
            " — at your current pace, usage hits 100% before the reset. The bar turns vivid red while there is still time to ease off.",
          ],
          [
            { b: "Extra usage is paying" },
            " — you are past your included quota and still working. The bar turns ",
            { b: "clay" },
            ", because red means stopped and clay means this is costing money. The tooltip says since when, and the number reports the extra-usage allowance on its own scale rather than the 0–100% of a quota the account no longer has.",
          ],
          [
            { b: "Blocked" },
            " — a window is spent and nothing is paying past it. It stays blocked on the window that still has room: a week at 47% behind a spent session wears the red chip and says so in words. The tooltip deliberately gives no percentage and no window name there, because naming ",
            { i: "Week 7d" },
            " would caption the wrong number.",
          ],
        ],
      },
      {
        heading: "Where the colour is",
        body: [
          "The percentage is drawn as a vector at the exact size the tray requests, so it stays sharp at 125–200%. Its colour is the window it is about — white for the session, yellow for the week, and orange whenever extra usage is paying, which is the one signal on the tile that still works when ",
          { i: "0 left" },
          " leaves no fill bar to carry it. The fill itself is the forecast.",
        ],
      },
    ],
  },
  {
    slug: "statistics",
    title: "Statistics: burn-up charts, live throughput and what each session cost",
    description:
      "Each rate-limit window as a burn-up chart with a dashed projection, a three-minute live throughput strip split by repo and token type, and one row per conversation priced at Anthropic's published API rates.",
    ogTitle: "Claude Code Tray: Statistics",
    ogDescription:
      "Burn-up charts, live tok/s, and one row per conversation with what it cost at list prices.",
    eyebrow: "Consumption pace",
    heading: "Statistics",
    anchor: "statistics",
    shot: {
      src: "/claude-tray/shots/statistics-7d.png",
      alt: "Statistics — the 7-day week burn-up chart with the projection stepping around the hours you usually work",
      caption:
        "The weekly burn-up: real utilization against the even-pace line, with the projection flat through the shaded overnight stretches.",
    },
    lead: [
      "A number says where you are. A chart says how you got there and where the line is going. Four tabs, all of them drawn from readings you have already paid for and transcripts already on your disk — no extra API calls.",
    ],
    sections: [
      {
        heading: "The two window tabs",
        body: [
          "The ",
          { b: "5-hour session" },
          " and the ",
          { b: "7-day week" },
          ", each as a burn-up chart: your real utilization against the even-pace line, with a dashed projection of where your current pace lands, and its own verdict chip. Last week's burn-up sits behind this week's as a faint line on the ",
          { b: "same axes" },
          ", so “worse than last week?” is a glance rather than a memory test.",
        ],
      },
      {
        heading: "Past the included quota",
        body: [
          "The clay extra-usage line is drawn on a right-hand scale of its own and never mixed into the 0–100%: an allowance with its own limit and its own reset is not the same quantity as the quota included in your plan. When the API says you are over but never says by how much, the chart shades the stretch instead and names no figure. Being over is a fact about the account rather than about one window, so both tabs draw it.",
        ],
      },
      {
        heading: "Throughput — what is running this minute",
        list: [
          [
            "Two three-minute charts of the ",
            { b: "rolling rate" },
            " sampled once a second, each ceiling ruled and labelled in ",
            { b: "tok/s" },
            ".",
          ],
          [
            "The upper chart is ",
            { b: "one line per repo" },
            " — the heaviest four, direct-labelled with their own rates, the rest as one grey “others”; each keeps its colour while it is on screen, so nothing changes hands mid-read.",
          ],
          [
            "The lower chart splits the same rate ",
            { b: "by token type" },
            ", which is where the cost of the cache re-read per turn becomes visible.",
          ],
          [
            "It scrolls because the axis is ",
            { b: "time" },
            " — the motion is the data, not a spinner. A line slides down through a pause because the work really is ageing out of the 60-second window. Idle goes flat, and it only repaints while the window is on screen.",
          ],
        ],
      },
      {
        heading: "Sessions — where it actually went",
        body: [
          "One row per conversation instead of a total, named by the title Claude Code gave it under the project it ran in. A workflow's agents fold into the session that spawned them, so a fan-out is one row and not twelve; open a row and it unfolds into the tasks that produced it, each with its ",
          { b: "own cost beside its subtree's" },
          " — which is how you tell a cheap coordinator from an expensive fleet. Below the list, which ",
          { b: "kind" },
          " of work ate the range: one row per task kind and per named slash command, with the effort its calls ran at, because the expensive command and the cheap prompt land in the same repo.",
        ],
      },
      {
        heading: "The dollar column is not a bill",
        body: [
          "The last column reads ",
          { b: "≈ $ at list prices" },
          ": what those tokens come to at Anthropic's published API rate card, priced per model and summed, so a $2 conversation and a $40 one stop looking alike. A subscription exposes no dollar balance and the tray knows nothing about your plan, your seat or your invoice — the ⓘ beside the range picker says exactly that, along with the date the rates were read. It reports what ran and never suggests running cheaper.",
        ],
      },
    ],
  },
  {
    slug: "notifications",
    title: "Notifications: seven toasts, and only four of them celebrate",
    description:
      "Four reset toasts with their own colour and headline so you can read them without reading a word, and three that state a fact instead of celebrating one — a heavy startup context, extra usage starting, and a machine-wide profile switch confirming itself.",
    ogTitle: "Claude Code Tray: the notifications",
    ogDescription:
      "Four resets with a colour each, and three that deliberately have no confetti.",
    eyebrow: "Reset alerts",
    heading: "The notifications",
    anchor: "notifications",
    shot: {
      src: "/claude-tray/shots/notify-surprise.png",
      alt: "The Surprise! toast — the weekly limit reset ahead of its scheduled deadline",
      caption:
        "Each kind has its own colour and headline, so what happened is legible before the words are.",
    },
    lead: [
      "An interruption has to earn itself. Four of these are worth being interrupted for because quota came back and you would otherwise find out by trying; three are worth it because something changed that you cannot see from the screen you are on.",
    ],
    sections: [
      {
        heading: "The four resets",
        list: [
          [
            { b: "Surprise!" },
            " — your weekly limit reset ahead of its scheduled deadline, a known Claude Code quirk worth catching.",
          ],
          [
            { b: "Bonus!" },
            " — a partial mid-window credit dropped your weekly usage. Found money.",
          ],
          [{ b: "New week!" }, " — the calm, routine weekly reset."],
          [
            { b: "Fresh session!" },
            " — the 5-hour window rolled over, fresh for the next five hours.",
          ],
          [
            "All four carry a confetti burst and the quota bar visibly refilling, all four are ",
            { b: "on by default" },
            ", and each is toggled independently in Settings.",
          ],
        ],
      },
      {
        heading: "The three without confetti",
        list: [
          [
            { b: "Heavy startup context" },
            " — a project's instruction files, memory and skills have grown enough that every session pays for them before you type a word, with what it costs to load on a cold cache. At most once per project per week, and ",
            { b: "off by default" },
            ": nobody asked to be told their own files are growing.",
          ],
          [
            { b: "Extra usage has started" },
            " — it exists because the four above do. The app interrupted you to say quota came back and said nothing when you started paying. Fires ",
            { b: "once" },
            " when a check first finds you past your included quota; not on a timer, not again while the same spell lasts. A receipt, not a prompt: it names no account and suggests nothing.",
          ],
          [
            { b: "Profile set for Windows" },
            " — writing the machine-wide profile is the least visible thing this app does, so it confirms itself: only when you switch by hand, once per switch, and only after the variable is read back to check the write took. Deliberately ",
            { b: "no quota bar" },
            " — a bar animating one account's remaining quota into another's would suggest switching accounts because one ran out, and this app does not say that.",
          ],
        ],
      },
    ],
  },
  {
    slug: "context",
    title: "Context: what every session costs you before you type",
    description:
      "Claude Code loads your instruction files, memory index and every skill description before the first prompt. The context audit measures that toll per project, says which parts were ever used, and hands the fix to Claude Code rather than editing anything.",
    ogTitle: "Claude Code Tray: the context audit",
    ogDescription:
      "The invisible toll on every request of the session, measured — and never edited.",
    eyebrow: "Local · Private",
    heading: "The context audit",
    anchor: "context",
    shot: {
      src: "/claude-tray/shots/context.png",
      alt: "The Context Load window: the session-zero gauge, the per-source breakdown and the findings",
      caption:
        "Session zero: what is loaded before your first prompt, broken down by where it came from.",
    },
    lead: [
      "Claude Code loads your instruction files, your memory index and every skill's description before your first prompt. It is a toll paid on every request of the session, and it is normally invisible — which is why it grows.",
    ],
    sections: [
      {
        heading: "What it measures",
        list: [
          [
            { b: "Eager vs lazy" },
            " — a 300 KB memory directory can cost less than one bloated ",
            { code: "AGENTS.md" },
            ", because only one of them is read before you type.",
          ],
          [
            { b: "Was it ever used?" },
            " — skills annotated ",
            { b: "45×" },
            " or ",
            { b: "never" },
            ", mined from your own transcripts rather than guessed from the file.",
          ],
          [
            { b: "What-if" },
            " — tick rows and watch the gauge and the cost drop, before changing anything on disk.",
          ],
          [
            { b: "Grade and drift" },
            " — A–F per project, and “+2.4 KB in the last 7 days”, because the number that matters is the direction.",
          ],
          [
            { b: "Between projects" },
            " — duplicated memory directories and dead project directories, which are invisible from inside either one.",
          ],
        ],
      },
      {
        heading: "It measures; it does not edit",
        body: [
          "Every finding comes with one plain sentence and the concrete fix, and the only action on the window is ",
          { b: "Copy cleanup prompt" },
          " — which hands the paths and the numbers to Claude Code. The scan reads only sizes, names, timestamps and frontmatter from ",
          { code: "~/.claude" },
          ", never the contents of your memories, instructions or skills.",
        ],
      },
    ],
  },
  {
    slug: "privacy",
    title: "Privacy: what this reads, what it doesn't, and what it sends",
    description:
      "Token counts, model ids and flags — never message content, with one visible exception. One 1-token API call every five minutes, history on local disk, and nothing uploaded.",
    ogTitle: "Claude Code Tray: privacy",
    ogDescription:
      "One 1-token call every five minutes. Everything else is computed on your machine.",
    eyebrow: "Privacy by design",
    heading: "What it reads, and what it sends",
    anchor: "privacy",
    lead: [
      "A tool that watches your usage has to be specific about what it looks at, because “we respect your privacy” is not a claim anyone can check. This page is the list.",
    ],
    sections: [
      {
        heading: "What leaves your machine",
        body: [
          "A single ",
          { b: "1-token API call every five minutes" },
          " — the rate-limit reading, which is the one thing that cannot be computed locally. That is all. There is no account to create, no telemetry endpoint, and nothing to log into: the app reuses the OAuth token Claude Code already stored, and never writes to a configuration folder.",
        ],
      },
      {
        heading: "What it reads from your transcripts",
        list: [
          [
            "Token counts, model ids and flags — ",
            { b: "never your message content" },
            ".",
          ],
          [
            "One visible exception: the ",
            { b: "Sessions" },
            " list names each conversation, using the title Claude Code generated for it or the prompt that opened it, because a list you cannot name is a list you cannot search. Both are capped at 200 characters before they are stored, they appear on that one screen, and nothing further into a conversation is ever read.",
          ],
          [
            "The 24-hour insights scan is bounded to files touched in the last 24 hours and runs in the background.",
          ],
          ["Usage history is stored locally on disk and never uploaded."],
        ],
      },
      {
        heading: "What the context scan reads",
        body: [
          "Sizes, names, timestamps and frontmatter from ",
          { code: "~/.claude" },
          " — ",
          { b: "never the contents of your memories, instructions or skills" },
          " — and it never edits them. The one action it offers is copying a cleanup prompt for Claude Code to act on.",
        ],
      },
      {
        heading: "What the page in Settings hides",
        body: [
          "Account holder, email and absolute paths are masked until you ask for them, because System information is the page that ends up in a screenshot. ",
          { b: "Copy for a bug report" },
          " copies the page exactly as shown, so a masked holder stays masked.",
        ],
      },
    ],
  },
];
