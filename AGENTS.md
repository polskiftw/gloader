# The First Commandment

> **Thou shalt seek the newest truth before acting upon the old.**

Whenever Claire asks for the **latest**, **newest**, **most recent**, **current**, **up-to-date**, **bleeding-edge**, or equivalent version of anything in this repository, do not assume the default branch or latest release is authoritative.

Before choosing what to use, inspect all relevant sources of newer work, including:

- the default branch;
- other branches;
- open pull requests and their head branches;
- releases and tags when relevant.

Use the newest relevant state of the work, even when it is unmerged, unless Claire explicitly names a specific release, tag, branch, commit, or other fixed version.

**Thus is the law: "latest" means latest across the whole repository, not merely latest merged.**

# The Second Commandment

> **Thou shalt not overthink the six seed problem.**

Whenever Claire refers to the **six seed problem**, first read and follow:

`gmods/ExpandedWorlds/SIX-SEED-PROBLEM.md`

That document is the canonical definition of the task.

# The Third Commandment

> **Thou shalt remember where the private Terraria binary lives.**

This repository has an **unpublished draft release** named/tagged:

`gloader-private-input`

It contains the private retail Terraria binary as a real release asset, currently:

`Terraria.zip`

Agents and repository workflows may use that draft asset whenever a task genuinely requires the matching retail Terraria binary (for example private x64-runtime construction, source/runtime verification, or stress testing).

Rules:

- **Do not publish the draft release.** It exists only as private repository input.
- **Do not commit the retail Terraria binary into the repository.**
- Use the actual release **asset**, not a file pasted into the release-description body.
- If a GitHub Actions workflow must enumerate/read this draft release, its `GITHUB_TOKEN` needs sufficient repository contents permission. In this repo, `contents: read` did not expose the draft; `contents: write` did.
- Prefer this existing private input over asking Claire to re-upload or externally host the same Terraria binary again.

**Thus is the law: if the retail Terraria binary is needed, look for `gloader-private-input` first.**
