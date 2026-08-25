import { useState } from "react";

function fallbackCopy(text: string) {
  const ta = document.createElement("textarea");
  ta.value = text;
  ta.style.position = "fixed";
  ta.style.opacity = "0";
  document.body.appendChild(ta);
  ta.select();
  try {
    document.execCommand("copy");
  } catch {
    /* nothing more to try */
  }
  document.body.removeChild(ta);
}

export function CopyButton({ text, label }: { text: string; label: string }) {
  const [copied, setCopied] = useState(false);

  const onClick = async () => {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      } else {
        fallbackCopy(text);
      }
    } catch {
      fallbackCopy(text);
    }
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  };

  return (
    <button
      type="button"
      className={copied ? "copy-btn copied" : "copy-btn"}
      onClick={onClick}
      aria-label={label}
    >
      <span className="ic">{copied ? "✓" : "⧉"}</span>
      <span className="lbl">{copied ? "Copied!" : "Copy"}</span>
    </button>
  );
}
