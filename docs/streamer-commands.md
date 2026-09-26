# Team Solve Streamer Commands

Only the streamer and moderators can use `!teamsolve` in Twitch chat to control Team Solve and choose who can contribute. All replies and announcements appear publicly in chat.

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

Team Solve remembers its on/off state, selected access option, and saved list. After a restart, it restores those choices automatically: off stays off, and on resumes once the puzzle connection is available.

On the very first setup, Team Solve is off and access is set to an empty list. If you turn it on with `!teamsolve list` before supplying any names, only the streamer and moderators can contribute.

## If the puzzle connection is unavailable

Turning Team Solve on without a puzzle connection makes it wait and start automatically when the connection becomes available. If the connection drops while Team Solve is on, contributions pause and resume automatically when it returns.

Use `!teamsolve off` to stop it from starting or resuming. Puzzle moves sent while disconnected are not saved for later.

Chat receives announcements when access changes and when Team Solve starts, stops, waits for a connection, or resumes.
