# Puzzle command protocol

Status: First draft, version 1.

This document defines the JSON puzzle-action messages sent from Streamer.bot to Tampermonkey and the acknowledgements returned by Tampermonkey. Component ownership is described in [the component design](design.md); human-facing syntax is defined in [the chat command language](../docs/twitch-command-language.md). Session activation/resume, session release/close, heartbeat, and readiness message formats are defined in the prototype's [connection protocol](connection-protocol.md).

## Message envelope

Each command message contains exactly one action. Multiple pencil-mark values or two line endpoints are part of a single action, not a batch. Each request has one request ID and one terminal acknowledgement when a response can be delivered.

For `$r2c6 5`, Streamer.bot sends:

```json
{
  "type": "puzzleAction",
  "version": 1,
  "requestId": "request-123",
  "sessionId": "session-456",
  "puzzleId": "puzzle-789",
  "action": {
    "kind": "value",
    "operation": "set",
    "target": { "row": 2, "column": 6 },
    "value": "5"
  }
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `type` | String | `puzzleAction` for a command; `actionAcknowledgement` for its reply. |
| `version` | Integer | Protocol version, initially `1`. |
| `requestId` | String | Unique identifier assigned by Streamer.bot to this action. Never reused for another action. |
| `sessionId` | String | Identifies the intended browser connection session established by the connection layer. |
| `puzzleId` | String | Identifies the loaded puzzle instance reported by Tampermonkey. |
| `action` | Object | One action, required on commands. |

Identifiers are nonempty opaque strings; the illustrative IDs above do not prescribe their format. Twitch usernames, permissions, and original chat text stay in Streamer.bot for feedback and diagnostics.

Team Solve must be enabled independently in Streamer.bot through the Twitch command interface and in the SudokuPad tab UI. Streamer.bot dispatches puzzle commands only while its switch is on and the enabled browser has an accepted, ready session. Enabling or disabling either side does not change the other side's switch or chat access policy.

Only one browser session is active at a time. Enabling Team Solve in a tab explicitly requests activation of its current puzzle; merely opening a socket or reporting readiness does not activate it. Accepting a new selection revokes the old session, closes its pending requests without retrying, and sends the old browser a session-close message. On receiving the close, the replaced tab disables its browser switch, discards queued actions, and stops automatic reconnection. Puzzle messages are sent only to the accepted, ready session, and old replies cannot restore ownership.

Automatic reconnection may resume the still-current selection after a transient disconnect while the browser switch remains enabled, but cannot take ownership from another tab. A rejected resume disables the tab and requires a new explicit enable action. A replacement, load, or reload of a puzzle clears browser enablement and requires explicit activation for that puzzle instance. Session selection does not change Streamer.bot's saved enabled state or access policy.

Disabling the browser immediately prevents further edits from starting, discards queued actions, releases its session, cancels reconnection, and closes the socket. Streamer.bot closes pending requests without replaying them when the session is released or the connection closes. An edit already applied is not undone by disabling the tab. Turning Streamer.bot off stops new dispatch while the browser may remain enabled and connected.

Tampermonkey generates a fresh `puzzleId` whenever a puzzle loads, including a reload of the same puzzle, and publishes it with readiness information. This is an instance identifier, not a puzzle URL or content hash. A new browser connection has a fresh session identity. Streamer.bot includes the current session and puzzle identifiers in every command.

Tampermonkey checks browser enablement and both identifiers on receipt and again immediately before execution, and requires current puzzle readiness before editing. A queued action must never be applied to a replacement puzzle. A puzzle mismatch returns `failed` with reason `puzzleChanged`; replies echo the identifiers of the original request, not those of the replacement puzzle.

## Targets

Rows and columns are one-based positive integers. Positions are integers from `1` through `9`, in reading order within the cell; `5` is the center.

Values, pencil marks, and cell colors use a cell target:

```json
{ "row": 2, "column": 6 }
```

X and O marks use a positioned cell target:

```json
{ "row": 2, "column": 6, "position": 4 }
```

Lines use exactly two positioned endpoints:

```json
{
  "start": { "row": 2, "column": 1, "position": 5 },
  "end": { "row": 2, "column": 7, "position": 5 }
}
```

Endpoint order does not change the identity of a line. Streamer.bot fills in default positions explicitly; Tampermonkey does not infer missing positions. Ordinary cell targets omit `position`.

## Actions

Every action has `kind`, `operation`, and `target`. The following table defines the permitted combinations and additional fields. Fields that do not apply are omitted rather than supplied as nulls or placeholders.

| `kind` | Operations | Target | Additional fields |
| --- | --- | --- | --- |
| `value` | `set`, `clear` | Cell | `value` for `set`; none for `clear`. |
| `cornerMarks` | `add`, `remove`, `clear` | Cell | `values` for `add`/`remove`; none for `clear`. |
| `centerMarks` | `add`, `remove`, `clear` | Cell | `values` for `add`/`remove`; none for `clear`. |
| `color` | `add`, `remove`, `clear` | Cell | `color` and `colorSet` for `add`/`remove`; none for `clear`. |
| `x`, `o` | `add`, `remove` | Positioned cell | `color` for `add`; none for `remove`. |
| `line` | `add`, `remove` | Two positioned endpoints | `color` for `add`; none for `remove`. |

Values are single-character strings: `"0"` through `"9"` or `"A"` through `"I"`. Streamer.bot normalizes letters to uppercase. A `values` field is a nonempty array of these strings; repeated entries represent the same mark and should be deduplicated during normalization.

Cell `color` is an integer from `0` through `9`; `colorSet` is an integer from `1` through `3`. X, O, and line colors are integers from `1` through `9` and do not have a `colorSet` field. Streamer.bot makes defaults explicit: cell color set `1`, drawing color `1`, and position `5` where applicable.

Operations express intent rather than toggling existing state. Removing an X, O, or line removes it regardless of color. Clearing cell colors clears every color set. Clearing a value or pencil-mark kind affects only that kind.

For `$r3c4 ^159`, the action is:

```json
{
  "kind": "cornerMarks",
  "operation": "add",
  "target": { "row": 3, "column": 4 },
  "values": ["1", "5", "9"]
}
```

For `$r3c4 -^`, the action is:

```json
{
  "kind": "cornerMarks",
  "operation": "clear",
  "target": { "row": 3, "column": 4 }
}
```

For `$r2c1p6:r2c7p4 c3`, the action is:

```json
{
  "kind": "line",
  "operation": "add",
  "target": {
    "start": { "row": 2, "column": 1, "position": 6 },
    "end": { "row": 2, "column": 7, "position": 4 }
  },
  "color": 3
}
```

## Acknowledgements

Tampermonkey replies after processing, not merely after receiving or queuing the action. It echoes `requestId`, `sessionId`, and `puzzleId` so Streamer.bot can match the response to the pending action and its original context.

```json
{
  "type": "actionAcknowledgement",
  "version": 1,
  "requestId": "request-123",
  "sessionId": "session-456",
  "puzzleId": "puzzle-789",
  "outcome": "understood"
}
```

| `outcome` | Meaning |
| --- | --- |
| `understood` | The action was processed, including valid actions that have no effect on the puzzle. |
| `failed` | The request was invalid or could not be executed. |

Failures include a stable `reason` code and may include a diagnostic `description`. Code branches use `reason`, not the description text.

```json
{
  "type": "actionAcknowledgement",
  "version": 1,
  "requestId": "request-123",
  "sessionId": "session-456",
  "puzzleId": "puzzle-789",
  "outcome": "failed",
  "reason": "puzzleNotReady",
  "description": "The puzzle is still loading."
}
```

The agreed reasons are `puzzleNotReady` and `puzzleChanged`. Additional stable codes for malformed messages, unsupported versions or actions, session mismatches, and execution failures remain to be named before implementation.

## Validation and completion

Tampermonkey validates the entire action before changing the puzzle: protocol version, session and puzzle identity, supported kind/operation combination, target shape, required fields, field types, and value ranges. Invalid actions fail as a whole; valid fields within an invalid request are not applied separately. Wire names and enum strings use the exact casing shown here.

Structural errors such as a negative row number return `failed`. Valid actions that cannot affect the current board, such as changing a given digit or targeting a positive coordinate outside the playable grid, return `understood` without a visible change.

Malformed JSON or missing correlation identifiers may prevent a correlated acknowledgement. Such messages are rejected and logged without changing the puzzle or inventing identifiers; any pending request on Streamer.bot eventually times out.

Tampermonkey processes actions in received order. Streamer.bot records a local timeout if no acknowledgement arrives by the pending request's deadline; the ordered dispatcher checks these deadlines through the command processor's `DoWork()`. Timeouts are local outcomes, not acknowledgements fabricated on behalf of the browser.

A timeout or disconnect does not prove that the puzzle was unchanged: execution may have happened before the reply was lost. Streamer.bot does not automatically retry. Each pending request is completed at most once; late, duplicate, or unmatched acknowledgements are diagnostic events and cannot complete a request again. Acknowledgements from an old session or puzzle cannot complete a new request.
