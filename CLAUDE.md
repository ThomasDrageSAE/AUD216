# CLAUDE.md — 26T1 AUD216 Project 1 (Unity 6 sound-design game)

This file is loaded automatically at the start of every Claude Code session. It is
deliberately short: the project's real orientation material lives in
[`Documentation/`](Documentation/README.md). Read that README's **Start here** list
before touching code — it points at `CODE_STRUCTURE.md` (project map, audio
architecture, single-authority rules), the editing ground rules in
`IMPROVEMENT_PLAN_2.md`, and `TERRAIN.md` (why nothing under `Terrains/TerrainLayers/`
may be deleted).

## Asking the owner questions — multichoice only

Every question put to the owner must be **multichoice with a recommended answer**.
Never ask an open-ended question. Full spec with examples:
[`Documentation/README.md` → "Asking the owner questions"](Documentation/README.md#asking-the-owner-questions).

The short version:

1. **Use the `AskUserQuestion` tool** whenever it is available. Put the recommended
   option **first** with the label suffix ` (Recommended)`; give every option a
   one-line `description` of what choosing it means; 2–4 options per question;
   batch related questions into one call.
2. **When the tool is not available** (plain-text reply, a ⏸ checkpoint written into a
   doc, a commit message), use this exact shape:

   ```
   **Q1. <the question>?**
   - **A.** <option> — <what it means / trade-off>  ← **recommended:** <one-line why>
   - **B.** <option> — <what it means / trade-off>
   - **C.** <option> — <what it means / trade-off>
   ```

   The owner answers with a letter (`Q1 A, Q2 C`) or free text; "Other" is always
   implied.
3. Exactly one recommended option per question, always with the reason. Options are
   mutually exclusive unless the question says **pick any**.
4. Don't ask what the repo, the code, or `git log` can answer. Collect questions
   while working and ask them together at the end of the turn; interrupt mid-task
   only when the answer changes the work materially (blocking), and say so.

## Hard rules (details in the docs)

- Run `bash Documentation/tools/compile-check.sh` after every edit (macOS or Windows
  Git Bash). It needs the editor-generated `.csproj` files and exits 1 without them —
  a fresh clone must be opened in Unity once on this machine first.
- Never rename or remove a `[SerializeField]` without grepping the `.unity`/`.prefab`
  files, `Assets/Scripts/Editor`, and `Assets/Scripts/Testing` first.
- Never touch `.meta` files, `.terrainlayer` files, or `…/Terrains/TerrainLayers/`.
- Legacy Input Manager only; no `.asmdef`s; no `DontDestroyOnLoad` on gameplay
  singletons (single-scene game, reload is the restart path).
- Items marked ⏸ in the work orders are **owner checkpoints** (in-editor play/listen
  checks). Stop and hand back — do not fake them.
- Several `.cs` files are CRLF; `core.autocrlf=true` on the owner's Windows machine.
  Check `file <path>` against `git show HEAD:<path> | file -` after editing.
- This is a **sound-design** assessment project: audio-state bugs outrank everything,
  and profile-name strings stay as the student-facing authoring surface.
