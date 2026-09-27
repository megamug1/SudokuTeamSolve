# Team Solve Streamer Commands

Only the streamer and moderators can use `!teamsolve` in Twitch chat to control Team Solve and choose who can contribute. All replies and announcements appear publicly in chat.

Team Solve must also be enabled in the SudokuPad tab UI for the current puzzle. The Twitch commands control Streamer.bot's switch; the tab control owns a separate browser switch. Contributions require both switches and an accepted, ready puzzle connection. Changing either switch does not change the other.

## Commands

In these examples, `Alice Bob` means one or more Twitch usernames separated by spaces.

| Command | What it does |
| --- | --- |
| `!teamsolve` | Shows whether Team Solve is off, on, or waiting for a puzzle connection, along with the selected access option and saved list. Does not change anything. |
| `!teamsolve everyone` | Allows everyone to contribute, saves that choice, and turns Team Solve on. |
| `!teamsolve followers` | Allows followers to contribute, saves that choice, and turns Team Solve on. |
| `!teamsolve subscribers` | Allows subscribers to contribute, saves that choice, and turns Team Solve on. |
| `!teamsolve list` | Allows the people on the saved list to contribute, saves that access choice, and turns Team Solve on. Keeps the saved names. |
| `!teamsolve list Alice Bob` | Replaces the entire saved list with these people, selects list access, and turns Team Solve on. |
| `!teamsolve off` | Turns Team Solve off and cancels any waiting to start or resume. Keeps the selected access option and saved list. |

## Who can contribute

Only one access option applies at a time: everyone, followers, subscribers, or the saved list. The streamer and moderators can always contribute while Team Solve is on.

Choosing an access option takes effect immediately and turns Team Solve on if it was off. Switching to everyone, followers, or subscribers keeps the saved list for later use. To change the saved names, send `!teamsolve list` followed by the complete list you want.

Use `!teamsolve` without any parameters to view the saved list without turning Team Solve on, even when another access option is selected.

## Between sessions

Streamer.bot remembers its on/off state, selected access option, and saved list. After a restart, it restores those choices automatically: off stays off, and on resumes once an enabled tab has an accepted, ready puzzle connection. Every puzzle load or reload starts with the browser switch off and requires explicit enablement in that tab.

On the very first setup, Team Solve is off and access is set to an empty list. If you turn it on with `!teamsolve list` before supplying any names, only the streamer and moderators can contribute.

## If the puzzle connection is unavailable

Turning Team Solve on without a puzzle connection makes it wait. Enable Team Solve in the SudokuPad tab to request a connection for that puzzle. Contributions start when the selected tab is accepted and ready. After a transient disconnect, the still-selected tab can reconnect and resume while both switches remain enabled. If another tab replaces it or its resume is rejected, it must be explicitly enabled again before it can become the selected tab.

Use `!teamsolve off` to stop Streamer.bot from starting or resuming contributions; the browser may remain enabled and connected. Disabling the tab releases its session and stops reconnecting without changing Streamer.bot's saved switch. Puzzle moves sent while disconnected are not saved for later.

Chat receives announcements when access changes and when Team Solve starts, stops, waits for a connection, or resumes.
