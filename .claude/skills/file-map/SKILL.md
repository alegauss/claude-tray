---
name: file-map
description: The per-file map of Claude Code Tray's own sources — every file under src/, one row each, with what it is responsible for and the rules its type carries. Use when you need to know which file owns a behaviour, where a new file goes, or what a type already does before writing a second reader of the same thing.
---

# File map (Claude Code Tray)

The map is **[`reference/file-map.md`](../../../reference/file-map.md)**. Read it there, and edit it
there: a new source file gets its row in that file, and `--selftest` fails a file under `src/` that no
row names.

It lives outside `.claude/` because Claude Code asks for approval on every edit under that folder, and
the map changes with nearly every new source file. This skill only points to it.
