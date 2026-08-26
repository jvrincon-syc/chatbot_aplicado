# docs/rules

Non-negotiable rules for this repository. Working *style* lives in the `AGENTS.md` files;
architecture context lives in `CLAUDE.md`. This folder holds the hard checklists agents must apply.

- [`SECURITY_AND_DATA.md`](SECURITY_AND_DATA.md) — secrets, RAG scope authority, read-only DB
  access, local LLM exposure, logging, dependencies.
- [`CODE_REVIEW_CHECKLIST.md`](CODE_REVIEW_CHECKLIST.md) — what every change is reviewed against
  before it's considered done.

Reviewer/auditor agents (`revisor-codigo`, `auditor-seguridad-datos`) apply these files. Keep them
short and enforceable — if a rule can't be checked, it doesn't belong here.
