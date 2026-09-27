# Streamer.bot implementation and setup

The implementation is [TeamSolve.cs](../src/streamerbot/TeamSolve.cs). Paste the complete file into one **Execute C# Code** sub-action. Keep code caching enabled and enable precompilation on application start so `Init()` restores settings. Reference `Newtonsoft.Json.dll` from the Streamer.bot installation if the editor does not already resolve it. The code uses C# 7.3-compatible syntax.

## Action wiring

Create a dedicated **blocking action queue** for Team Solve. Every event wrapper below must run on this queue, set `teamSolveEvent`, and invoke the same core action immediately with its arguments. Do not copy the C# sub-action into each wrapper: that would create independent runtime state. Do not enqueue the core invocation again from inside the wrapper. Queue order provides chat ordering and serializes session replacement with dispatch; the components need no locks or background tasks.

| Trigger wrapper | `teamSolveEvent` | Arguments passed to the core action |
| --- | --- | --- |
| Twitch Chat Message | `chat` | `message`, `userId`, `userName`, `isModerator`, `isBroadcaster`, `isSubscribed`, `isFollowing` |
| Custom WebSocket Server Connection Opened | `opened` | `sessionId` |
| Custom WebSocket Server Connection Closed | `closed` | `sessionId` |
| Custom WebSocket Server Message | `browser` | `sessionId`, `data` |
| Repeating timed action, once per second, zero required chat lines | `tick` | none |
| Twitch Stream Online | `streamStarted` | none |

`userName` is the Twitch login, not display name. Role and membership arguments are booleans. Map the trigger's variables to these names in the wrapper where needed. Obtain `isFollowing` using **Twitch > Followers > Get Follow Age Info for Target**, with source **User**, before invoking the core. This uses the host's current lookup result rather than maintaining a second follower cache. Subscriber and moderator flags come from the chat event. If a configured lookup or required argument is missing, fix the action wiring; the implementation does not substitute permission defaults.

The Chat Message path handles both `$` commands and `!teamsolve`; do not also route the same message through a Command Triggered action. A wrapper may filter unrelated messages before the follower lookup. Keep the lookup on the same blocking queue so it does not reorder puzzle commands. A slow host lookup can delay timer processing; check this during live setup.

Create a dedicated **custom WebSocket server**, bound locally, for example `127.0.0.1:9090`. This is separate from Streamer.bot's standard WebSocket API. Select this server explicitly in all three connection/message triggers. Set `ServerIndex` at the top of `TeamSolve.cs` to its zero-based index. Enable server auto-start. The browser connects to this server and uses [the connection protocol](../src/connection-protocol.md).

The host's `sessionId` argument is a socket connection identifier. The JSON `sessionId` is a separate, newly generated Team Solve session identity. They must not be interchanged.

## Component map

All classes live in one file for pasting into Streamer.bot, but maintain separate ownership:

| Component | Owns |
| --- | --- |
| `CPHInline` | Host API adapters and top-level event routing |
| `Runtime` | Component assembly, chat routing, timer coordination |
| `Controller` / `SettingsStore` | Control commands and persistent preferences |
| `AccessValidator` / `CommandParser` | Permission decisions and pure command normalization |
| `PuzzleCommandProcessor` | Input-to-dispatch workflow and rejection outcomes |
| `OrderedDispatcher` | Action envelopes, pending requests, acknowledgements, completion deadlines |
| `ConnectionManager` / `BrowserSession` / `HeartbeatMonitor` | Selected browser, connection targeting, readiness, resume eligibility, liveness |
| `BrowserMessageRouter` | Wire decoding, envelope validation, message routing |
| `Transport` | Host WebSocket send/close operations |
| `ChatFeedback` | All public reply wording, delivery, and message splitting |
| `Diagnostics` | JSON Lines files and Streamer.bot debug output |

Internal components trust their collaborators. Chat syntax is validated at the parser; browser envelopes are checked once at the router, as specified by the design. Session identity and acknowledgement checks enforce lifecycle correctness. Settings corruption, configuration errors, and log failures surface instead of silently resetting or substituting state. The only exception recovery surrounds malformed incoming JSON and WebSocket sends, whose failures have defined lifecycle outcomes.

## First-version decisions

- Settings are one persisted global, `teamSolve.settings`, containing enabled state, access option, and lowercase login names. Rename tracking is not implemented; update the list after a username changes.
- A fresh installation is off with an empty list. Restoring enabled settings starts in Waiting until the browser is ready.
- Each action gets a unique ID before validation. Authorized actions dispatch immediately in queue order; acknowledgements may arrive independently. No disconnected backlog or automatic retry exists.
- Action replies expire after 15 seconds. Pending storage is limited to 256 requests; overflow gets a concise chat reply. This is a bound on outstanding work, not a per-user rate limit.
- Heartbeats start immediately after acceptance, then five seconds after each reply, with a ten-second reply deadline. A readiness update does not satisfy an outstanding heartbeat.
- Browser failures, timeouts, and uncertain delivery are diagnostic-only. Public chat reports controls, state changes, permissions, syntax, and pending-capacity rejection. Successful edits use the board as feedback.
- JSONL logs live in `TeamSolveLogs` under the application directory. Initialization starts a startup log; Stream Online starts a fresh per-stream file. Recompiling or restarting splits a stream into additional files. Logs retain command text and sender IDs, but do not log resume selection IDs or raw incoming frames.
- Browser execution is not implemented by this change. The new connection contract must be implemented by the userscript before end-to-end use.

## Verification

Run the standalone harness with .NET SDK 8:

```powershell
dotnet run --project tests/streamerbot/TeamSolve.Tests.csproj
```

It compiles the actual source, including the entry point against host API stubs, and runs parser, policy, lifecycle, heartbeat, acknowledgement, and failure scenarios. It uses the SDK's Newtonsoft.Json assembly and no downloaded test packages. This verifies local behavior; it does not substitute for compiling inside Streamer.bot and exercising the real trigger wiring.

For the live check, enable everyone while disconnected and expect Waiting. Activate a browser and publish readiness; expect On. Send two different values to one cell and verify their order. Disconnect and send another command; verify it is rejected and not replayed after resume. Replace the tab and verify the old tab disables itself. Finally turn Streamer.bot off and verify the browser remains connected but puzzle commands are rejected.

Host API references: [targeted custom-server sending](https://docs.streamer.bot/api/csharp/methods/core/websocket/custom-server/websocket-custom-server-broadcast), [closing a connection](https://docs.streamer.bot/api/csharp/methods/core/websocket/custom-server/websocket-custom-server-close-session), [message trigger](https://docs.streamer.bot/api/triggers/core/websocket/custom-server/message), and [follower lookup](https://docs.streamer.bot/api/sub-actions/twitch/followers/get-follow-age-info-for-target).
