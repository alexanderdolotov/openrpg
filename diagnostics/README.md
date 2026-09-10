# Diagnostics

Small, throwaway GDScript scripts (`extends SceneTree`) used to reproduce
and investigate a specific bug or behavior in isolation — run headless
via Godot's CLI, e.g.:

```
godot --headless --script diagnostics/diag_combat.gd
```

These are **scratch scripts, not an automated test suite** — there's no
runner, no assertions framework, and nothing here is checked in CI. They
exist because reproducing some bugs (timing, navigation, LLM fallback
behavior) is far faster as a standalone headless script than through
manual play-testing in the editor.

**Convention:** when re-investigating something, prefer editing the
existing `diag_<topic>.gd` in place over adding `diag_<topic>2.gd`. If a
new numbered version is genuinely needed (a fix landed and the old
repro is being kept as a "before" reference), delete the superseded one
once it's no longer needed rather than leaving both — that's how this
folder ended up with `diag_travel.gd` through `diag_travel4.gd`
simultaneously. A script only worth keeping around is one you'd expect
to reach for again; anything else should be deleted once its bug is
fixed and confirmed.
