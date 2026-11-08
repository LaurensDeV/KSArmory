---
description: Launch KSA (or reuse a running game) and report what is in the world, without taking over a game somebody else started.
---

Bring up a game and say what is in it. Optional argument: a save to load — `/fly` alone just
brings up whatever the loader starts with.

Save requested: $ARGUMENTS

Do this with the bridge's own tools, not by shelling out:

1. Call `mcp__ksa__ksa_status`. Do **not** poll `tasklist.exe` — that is 20 calls a session
   answering a question one call answers, and it tells you less.
2. **If a game is already running**: say so, report what `ksa_status` found, and stop. Do not
   restart it and do not run a scenario in it — it may be a game the user is playing, and a
   scenario run would take it over, override their settings and load its own save. Ask first.
3. **If nothing is running**: call `mcp__ksa__ksa_launch`, passing the save above if one was
   given. It builds, deploys and launches, and it refuses to start a second game.
4. Report the state `ksa_status` comes back with: the craft, whether it is in flight, the world
   time. If a save was asked for, say what is actually flying.
5. If the launch times out, read what the refusal tells you before retrying. The usual cause is
   the loader waiting on its configuration dialog, which `tools/scenario.sh` clears and
   `ksa_launch` does not.

If the user wants to *see* something rather than read about it, `mcp__ksa__ksa_capture` returns
the screenshot inline. The `server.py cli` form writes it to a folder it wipes on the next call,
so prefer the tool.
