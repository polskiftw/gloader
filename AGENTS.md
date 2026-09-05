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

> **Thou shalt not overthink the fourteen seed problem.**

Whenever Claire refers to the **fourteen seed problem**, first read and follow:

`gmods/ExpandedWorlds/FOURTEEN-SEED-PROBLEM.md`

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

# The Fourth Commandment

> **Thou shalt treat `main` as the destination, not the workspace.**

Normal repository-changing work must begin on a temporary branch created from the current `main`, unless Claire explicitly names another starting branch or explicitly asks for a direct change to `main`.

Use short-lived branches that describe the work, normally with prefixes such as:

- `feat/` for new features;
- `fix/` for bug fixes;
- `docs/` for documentation-only changes;
- `chore/` for maintenance and dependency/tooling updates;
- `experiment/` for work that may be discarded.

While work is in progress:

- commit and push early to the working branch as needed;
- run the relevant tests and GitHub Actions there;
- keep unfinished, exploratory, or failing intermediate work off `main`.

When the work is complete:

1. open a pull request into `main`;
2. verify the relevant checks pass;
3. merge the pull request when it is ready, unless Claire explicitly asks to keep it unmerged;
4. let GitHub automatically delete the merged head branch.

Do not create permanent `develop`, `dev`, `staging`, `next`, or similar branches merely to hold ordinary ongoing work. Releases belong in tags/releases, not permanent release-history branches. Superseded experiments and finished debug branches should be deleted rather than preserved as archaeology.

A temporary branch may contain the newest truth before it is merged, which is why the First Commandment still requires checking active branches and pull requests.

**Thus is the law: work on branches, prove it there, then bring finished work to `main`.**

# The Fifth Commandment

> **Thou shalt remember that GitHub is part of Claire's toolbox.**

For Claire's requests about this repository, treat the connected GitHub tooling as available whenever the task naturally requires repository access — inspecting code or docs, checking branches or pull requests, editing files, running or checking Actions, releases, or other repository work. Claire does **not** need to explicitly say **GitHub** or **`@GitHub`** every time.

When Claire does explicitly invoke **GitHub** or **`@GitHub`**, that is an especially direct instruction to attempt the requested repository work through the connector before claiming GitHub access is unavailable.

If one particular GitHub operation is not exposed by the connector in the current session, say exactly which operation is missing and use the available GitHub operations to accomplish the goal another way when practical. Do **not** turn the absence of one API action into the broader claim that GitHub itself is unavailable.

**Thus is the law: if Claire asks for repository work, use GitHub without making her remind thee that GitHub exists.**
