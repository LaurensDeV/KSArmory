# Multiplayer

**A plan, not a record.** Nothing here is built, and nothing here has been run across two machines.
It is what KSA's networking does as of build `2026.10.13.5602`, read from the decompiled source and
unchanged in `2026.10.19.5638`, what syncing this mod's rounds over it would take, and the little
that is worth doing before KSA's side exists.

Recheck the first section on every KSA update. It is the only part a build can falsify.

## What KSA has

A networking skeleton in `KSA.Networking`, about 1,400 lines over `Brutal.RakNet`, RocketWerkz's
wrapper around the RakNet UDP library. A player can connect, receive one copy of the host's world,
and chat. Nothing is synchronised after that.

It is reached only from the in-game console.

| Command | What it does |
| --- | --- |
| `nethost [ip] [port] [maxPlayers]` | host and play; defaults `127.0.0.1`, `50000`, 10 |
| `netded …` | the same as a dedicated server, with no local player |
| `netjoin [ip] [port]` | join a host |
| `netstop` | stop hosting, or disconnect |
| `netchat <message>` | a chat line to everyone |
| `netdebug` | print the player table |

What each part does:

- **Join.** The client sends a `JoinGameRequestMessage`. The host always accepts, builds
  `UniverseData.Create()` and sends it back compressed. The client calls
  `Universe.DeserializeSave` on it, so it starts with the host's world at that instant.
- **Players.** The host keeps a table of client id to `PlayerInfo`, which is a name and nothing
  else, and rebroadcasts it on every join and leave. The console commands hardcode the names.
- **Chat.** A request goes to the host, which prefixes the sender's name and broadcasts it. On
  receipt it is written to the log.
- **Replication.** The host sends a `ReplicationMessage` to every client each 50 ms. The message
  has no fields and its `Execute` is empty.
- **Authority.** `Authority.HasAuthority` is true offline and on the host. `Dispatch.ToAuthority`,
  `ToAllPlayers` and `ToNonAuthority` route a message and run it locally where that applies.

Outside the namespace, the only caller is `Network.Tick()` once a frame in `Program.OnFrame`. So
after the join each machine simulates its own copy: no vehicle, input, staging, pause or warp is
sent.

There is no password prompt, no encryption and no version or mod check on join. The default bind
address is localhost.

## What a mod message would take

- **Sending works.** `GameMessage` is public with a protected constructor taking a
  `GameMessageId`, and `Dispatch` is public. KSA uses ids 134 to 139; a mod message needs a byte
  above that.
- **Receiving needs a patch.** `MessageSerialisation.Deserialise` is a closed `switch` over KSA's
  six ids and throws `NotImplementedException` for any other, from inside `Network.Tick()`. It
  takes a Harmony prefix there or on `NetworkPeer.OnPeerPacket`. The first takes a `ref struct`
  and the second a `Packet*`; patching either is untried.
- **Every send is `ReliableOrdered` at high priority on channel 0**, and packets are read every
  50 ms. Good for events, poor for per-frame state: one lost packet holds up everything behind it.
- **A peer without this mod, or with a different weapon pack, hits that same throw** on the first
  message it receives.

## Why rounds have nothing to sync against

The transport is the small part. A round's state is only meaningful against a world both machines
agree on, and they do not.

- **Vehicles are not replicated.** A round is held as an offset from its launcher and steers at a
  `Vehicle` handle. On two machines that is two launchers and two targets.
- **There is no shared clock.** Pause, warp and simulation speed are local. A position sent in the
  ecliptic with 50 ms of clock disagreement is 1.5 km out, so anything sent has to be relative to
  the body or the launcher. `docs/FRAMES-AND-EPOCHS.md` is the same rule within one machine.
- **A craft the host flies does not move on the client**, so a working round sync could not be
  flown in an engagement, which is what `CLAUDE.md` asks of a behaviour change.

## What the mod would need once KSA replicates vehicles

1. **Round identity.** An id that means the same round on every machine: owner client and a
   counter.
2. **Events, not state.** "Fired", with launcher craft, part, tube, munition, target or aimpoint
   and simulated time; each peer flies its own copy. A gun's burst is a start and a stop, never a
   message per shell.
3. **Outcomes decided on the authority.** Detonations, shoot-downs, part failures and kills run
   where `Authority.HasAuthority` is true and are broadcast as results.
4. **Fire control in one place per craft.** Auto-engage, lock and the magazine belong to one peer,
   or two machines each fire the salvo.
5. **Corrections for long-lived guided rounds.** Periodic body-relative position and velocity from
   the authority, which needs a snapshot and restore on `IProjectile`.
6. **State for a joiner.** Rounds in the air, magazines, the team roster and per-system settings
   are not in `UniverseData`.
7. **A decision about the network rule.** `tools/check-network.sh` holds the mod to one request,
   made when a player clicks Send. A session the player started with `nethost` is consent of a
   different kind and needs its own stated exception.

## Worth doing before then

Ranked. Only the first two are work; the rest are things to hold to in review.

1. **Stable round ids and serialisable round state.** Pays today: rounds, magazines and tracks
   would survive a save and reload, which they do not. It is also the hardest to retrofit, because
   every `IProjectile` takes part. Do it when save persistence for rounds is wanted.
2. **World rules out of `Config`.** `Config` holds what one player sees and hears beside what is
   true of the world: `TeamNames`, `TeamRules`, `DamageScale`, `DamageIndividualParts`,
   `ShareTargetsAcrossWeapons`, `LimitWarpInFlight`. The first kind stays local and the second
   would be the host's. Split them the next time `Config` is reshaped.
3. **An outcome is data before it is applied.** A burst produces what it broke and where, relative
   to the body, and a separate step hands that to the engine. Most of `Sim/` is already this
   shape. It is what lets a host decide and a client only apply.
4. **Every command through one surface.** `Ui.FireGroup` is the only path from the panel to
   `FireAtLock`, and `Sim/BridgeCommand.cs` takes commands as text. A remote player's trigger is
   then one more caller. No new control writes to a system directly.
5. **Randomness seeded per round.** `SeekerLock` is the one place the mod rolls, and every
   seeker in every `WeaponSystem` draws from one static generator (`WeaponSystem._seduction`). Seeded from the round's id instead,
   two machines agree on a decoy without a message. Needs item 1.

## Not worth doing

- **Determinism.** The simulation depends on each machine's frame step. The design above is
  authority and events, which does not need lockstep.
- **Ids for vehicles or parts.** KSA has to provide those for its own replication.
- **Transport, an authority abstraction or message types.** All of it depends on what KSA ships.

## Single-player assumptions to revisit then

Each is right today and wrong with two players and one world clock. None should change until KSA
shows how it handles time and control.

- `Sim/WarpPolicy.cs` holding the world's speed down while rounds fly.
- The panel's slow-motion controls.
- The craft being flown and the craft that fired being dented and never broken by a burst.
- `Program.ControlledVehicle` read as "the player", named in 28 files under `src/KSArmory/`.
