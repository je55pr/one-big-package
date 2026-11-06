# Replay-backed bug reports

Run `tools/install-bug-capture-shortcut.ps1` once, then double-click **OBP Bug
Capture** on the desktop. It builds the current checkout quietly and records an
ordinary R&C1 level entry using remembered sources and a copy of the current
campaign. Recording is explicitly opt-in. The ordinary launcher is unchanged.

Press **F8**, or **Start then Square** on a controller, to report a problem.
The marker and observation are captured at the next completed process callback,
before the form opens. Enter a short title/optional description, or use **Cross**
on **Save Report** without typing. The form pauses gameplay and recording;
form inputs and elapsed form time are excluded. Closing it restores the prior
pause menu/focus and resumes capture at a physics boundary with neutral actions.
No separate F9/finalization step is necessary. Multiple reports may be saved
while continuing the same run.

**F7**, **paused Select**, or the **Reports** control opens the local inbox.
Select a report and choose **Replay Selected** to open a separate game process
from the original level entry, pausing at that report's marker for inspection.
Close the replay window to return to the inbox. It uses the
existing playtest reader and compares sampled observations without restoring
them. A match is evidence of repeatability, not exhaustive world equality or
retail parity. Different assemblies require deliberate confirmation; a different
engine/platform/physics backend is rejected. Replay still needs an authorized
matching R&C1 source on the receiving developer's machine.

The inbox opens with the report list focused. D-pad/left stick selects reports;
Cross/Enter or a mouse double-click replays the selected report. Right moves to
the action buttons, Left/Right follows each visible button row, and Up returns
from Replay/Export to the same list selection. Mouse clicks select rows and the
wheel scrolls. Completing an action restores its previous focus.

## Local files and sharing

The gitignored `captures/bug-reports/` inbox contains:

- `<session-id>/session.json`: atomic index of stable report IDs, UTC timestamps,
  descriptions, destination/marker steps and recording identity/size/SHA-256.
- One shared, completed `.run.jsonl.gz` prefix for that session. Adding another
  report atomically publishes a newer prefix and index, retaining earlier markers.
- `live/<session-id>/`: the ongoing input tape and isolated campaign. Closing the
  game finalizes this tape. Interrupted `.partial` tapes remain explicitly
  unfinished and are never offered as ready reports. Previously saved reports
  remain replayable after interruption. Partial crash recovery is not attempted.
- `exports/`: explicitly exported portable ZIPs, capped at **25,000,000 bytes**.
  Each ZIP contains only the selected report's manifest and the shared recording.
  Oversized exports fail while the local report remains available.
- `replay-results/`: local divergence/match results for inbox-launched replays.

Use **Open Folder** to retrieve files, or **Export Selected ZIP** to create a
portable package. A developer can use **Import ZIP** in their inbox, then
**Replay Selected**. The optional `tools/replay-bug-report.ps1` wrapper accepts
the session manifest and report ID for scripted triage. Same-build replay is
strict; a changed-build divergence does not by itself establish a failed fix.

Only whitelisted manifest and validated recording files enter ZIPs. No retail
assets, ISOs, source paths, arbitrary logs or screenshots are included. Review
your own description before sharing. Nothing uploads or creates GitHub issues.
Reports and exports remain until explicitly deleted. **Delete Session** requires
a second confirmation and removes that session's reports, snapshots and local
capture files; active sessions cannot be deleted. Exported ZIPs remain separate
and can be deleted in the opened folder.

## Manual verification

Use the live bug-report UI and the maintained `tools/launch-bug-capture.ps1`,
`tools/record-playtest.ps1` and `tools/replay-playtest.ps1` workflows to investigate
reproduction steps, replay markers, exports and game behaviour. Check the actual
rendering and controller experience manually. The old automatic bug-report,
menu and playtest smoke runners are [archived](https://github.com/je55pr/one-big-package/tree/tests) and no longer run
in CI or this branch.

Follow-ups: richer controller text entry, an inbox usable before entering a
recorded world, resumable crash recovery, and optional human-approved publication.
