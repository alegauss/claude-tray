// The Markdown twin, converted from the same render. The copy lives in the content
// module but the *composition* lives in the JSX, so the twin is produced from the rendered
// HTML — never authored a second time from the data, which would re-declare the composition
// and let the two drift. The nav and footer never reach a twin (dropped by tag) and the
// call to action is dropped by its data-twin="omit" attribute.
import { parse } from "node-html-parser";

// Whole subtrees that never belong in a twin.
const SKIP_TAGS = new Set(["NAV", "FOOTER", "BUTTON", "SCRIPT", "STYLE", "NOSCRIPT"]);
// Decorative chrome dropped by class: the pulse dot, the ✓ ticks, the emoji card icons,
// the step numbers, the privacy padlock, the section kicker, and the two colour chips (the
// projection state's swatch and the toast's tone swatch) that carry a colour and no words.
const SKIP_CLASSES = new Set([
  "dot", "chk", "ico", "n", "lock", "eyebrow", "bar", "legend", "copy-btn",
  "swatch", "nsw",
]);
// Elements that carry a run of text, not a block — rendered inline, never recursed as blocks.
const INLINE_TAGS = new Set(["SPAN", "B", "STRONG", "I", "EM", "CODE", "KBD", "A", "BR"]);
const HEADING = { H1: "#", H2: "##", H3: "###", H4: "####", H5: "#####", H6: "######" };

function decode(s) {
  return s
    .replace(/&#x([0-9a-fA-F]+);/g, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(parseInt(d, 10)))
    .replace(/&nbsp;/g, " ")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&amp;/g, "&");
}

const collapse = (s) => s.replace(/\s+/g, " ");
const classList = (node) => {
  const c = node.getAttribute && node.getAttribute("class");
  return c ? c.split(/\s+/) : [];
};

function skipped(node) {
  if (SKIP_TAGS.has(node.rawTagName?.toUpperCase())) return true;
  if (node.getAttribute && node.getAttribute("data-twin") === "omit") return true;
  return classList(node).some((c) => SKIP_CLASSES.has(c));
}

// Every text node under a subtree, tags dropped and entities decoded.
function plainText(node) {
  if (node.nodeType === 3) return decode(node.rawText);
  if (node.nodeType !== 1) return "";
  return node.childNodes.map(plainText).join("");
}

// The raw text of a subtree with markup left in place (no decode). node-html-parser keeps
// a <pre>'s whole content as a single raw text node — colour spans and all — so the fence
// is cleaned by stripping the tags out of this and only then decoding entities. Ordering
// matters: an escaped `&lt;` in the content must survive the tag strip and decode after it.
function rawTextAll(node) {
  if (node.nodeType === 3) return node.rawText;
  if (node.nodeType !== 1) return "";
  return node.childNodes.map(rawTextAll).join("");
}

function fencedFrom(node) {
  return fenced(decode(rawTextAll(node).replace(/<[^>]+>/g, "")));
}

// --- inline: a run with **bold**, *italic*, `code` and [links] ---
function inline(node) {
  if (node.nodeType === 3) return collapse(decode(node.rawText));
  if (node.nodeType !== 1) return "";
  if (skipped(node)) return "";
  const tag = node.rawTagName.toUpperCase();
  const kids = () => node.childNodes.map(inline).join("");
  switch (tag) {
    case "CODE":
    case "KBD":
      return "`" + collapse(plainText(node)) + "`";
    case "B":
    case "STRONG": {
      const t = kids().trim();
      return t ? `**${t}**` : "";
    }
    case "I":
    case "EM": {
      const t = kids().trim();
      // a lone decorative glyph (the ✗ before a non-goal) carries nothing in a flat file
      if (!t || (t.length <= 2 && !/[a-z0-9]/i.test(t))) return "";
      return `*${t}*`;
    }
    case "BR":
      return " ";
    case "A": {
      const href = node.getAttribute("href") || "";
      const text = kids().trim();
      if (!text) return "";
      return href && !href.startsWith("#") ? `[${text}](${href})` : text;
    }
    case "SVG":
      return "";
    default:
      return kids();
  }
}

const inlineTrim = (node) => collapse(node.childNodes.map(inline).join("")).trim();

function fenced(text) {
  const body = text.replace(/^\n+/, "").replace(/\n+$/, "");
  return "```\n" + body + "\n```";
}

function statusRow(node) {
  const mark = node.querySelector(".mark")?.text.trim() ?? "";
  const id = node.querySelector(".id")?.text.trim() ?? "";
  const what = node.querySelector(".what");
  const title = what?.querySelector("b")?.text.trim() ?? "";
  // the description is everything in .what after its leading <b> title
  const rest = what
    ? collapse(
        what.childNodes
          .filter((n) => !(n.nodeType === 1 && ["B", "STRONG"].includes(n.rawTagName.toUpperCase())))
          .map(inline)
          .join(""),
      ).trim()
    : "";
  return `- ${mark} **${id} ${title}**${rest ? ` — ${rest}` : ""}`;
}

function cellsOf(tr) {
  return tr.childNodes.filter(
    (n) => n.nodeType === 1 && ["TH", "TD"].includes(n.rawTagName.toUpperCase()),
  );
}

// A GitHub-flavoured markdown table, so the twin of a matrix is machine-readable. A row
// with a single spanning cell (a group header) becomes a bold label row.
function tableToMarkdown(table) {
  const headTr = table.querySelector("thead tr") ?? table.querySelector("tr");
  const headers = headTr ? cellsOf(headTr).map((c) => inlineTrim(c) || " ") : [];
  const ncol = headers.length;
  if (ncol === 0) return "";
  const lines = [
    `| ${headers.join(" | ")} |`,
    `| ${headers.map(() => "---").join(" | ")} |`,
  ];
  const bodyRows = table.querySelectorAll("tbody tr");
  const rows = bodyRows.length ? bodyRows : table.querySelectorAll("tr").slice(1);
  for (const tr of rows) {
    const cells = cellsOf(tr);
    let row;
    if (cells.length === 1) {
      row = [`**${inlineTrim(cells[0])}**`, ...Array(Math.max(0, ncol - 1)).fill("")];
    } else {
      row = cells.map((c) => inlineTrim(c) || " ");
      while (row.length < ncol) row.push("");
    }
    lines.push(`| ${row.join(" | ")} |`);
  }
  return lines.join("\n");
}

function blocks(node, out) {
  for (const child of node.childNodes) {
    if (child.nodeType === 3) {
      const t = collapse(decode(child.rawText)).trim();
      if (t) out.push(t);
      continue;
    }
    if (child.nodeType !== 1 || skipped(child)) continue;
    const tag = child.rawTagName.toUpperCase();
    const cls = classList(child);

    if (cls.includes("row")) {
      out.push(statusRow(child));
    } else if (cls.includes("term")) {
      const pre = child.querySelector("pre");
      if (pre) out.push(fencedFrom(pre));
    } else if (cls.includes("codeblock")) {
      const code = child.querySelector("code");
      if (code) out.push(fencedFrom(code));
    } else if (tag === "PRE") {
      out.push(fencedFrom(child));
    } else if (tag === "TABLE") {
      const md = tableToMarkdown(child);
      if (md) out.push(md);
    } else if (HEADING[tag]) {
      const t = inlineTrim(child);
      if (t) out.push(`${HEADING[tag]} ${t}`);
    } else if (tag === "P") {
      const t = inlineTrim(child);
      if (t) out.push(t);
    } else if (tag === "UL" || tag === "OL") {
      const items = child
        .querySelectorAll(":scope > li")
        .map((li) => inlineTrim(li))
        .filter(Boolean)
        .map((t) => `- ${t}`);
      if (items.length) out.push(items.join("\n"));
    } else if (tag === "FIGCAPTION") {
      const t = inlineTrim(child);
      if (t) out.push(`_${t}_`);
    } else if (tag === "SVG") {
      const label = child.getAttribute("aria-label");
      if (label) out.push(`> _Figure: ${collapse(label).trim()}_`);
    } else if (INLINE_TAGS.has(tag)) {
      // an inline element sitting at block level (a pill, a hero-meta item) is one line,
      // not a tree to recurse and shatter into a paragraph per text node
      const t = inlineTrim(child);
      if (t) out.push(t);
    } else {
      blocks(child, out);
    }
  }
}

export function htmlToMarkdown(html, { title } = {}) {
  // node-html-parser keeps <pre> content as raw text by default, which leaves the
  // terminal's colour spans in the fence; parse inside <pre> so plainText() drops them.
  const root = parse(html, { comment: false });
  const out = [];
  blocks(root, out);
  const cleaned = out.filter((b) => b.trim().length > 0);
  const body = cleaned.join("\n\n");
  return (title ? `# ${title}\n\n` : "") + body + "\n";
}
