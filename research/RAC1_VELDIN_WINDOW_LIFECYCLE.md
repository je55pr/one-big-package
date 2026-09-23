# R&C1 Veldin window-lifecycle diagnosis

## Human playtest observation

> "Also the game window repeatedly closes during Jess's test; investigate whether this is a crash/runtime exit."

## Retained pre-hardening evidence

MjauRunner run-002233 was an ordinary direct Veldin launch using
`--rac1-iso` plus `--destination rac1:LEVEL0`, with no smoke or capture
flags. It ran for about eight minutes and then exited with code 0 and no
MjauRunner stop reason. Standard error contained only nonfatal SDL gamepad
mapping errors. The Windows Application log had no Godot, .NET, or
Application Error record. Standard output contained no smoke/capture result,
navigation message, or explicit quit message.

That evidence does **not** prove a crash, an application-requested quit, or an
external close. Before this change, ordinary quit sites had no common
telemetry, and Godot accepted desktop close requests automatically.

The audit also found a raw Escape fallback in `OBPGame._UnhandledInput`.
The direct R&C1 destination route normally enables generic navigation and
`SourceManagerBootstrap` intercepts Escape first, so run-002233 cannot be
retroactively attributed to that fallback from the retained logs alone.

## Hardened lifecycle contract

`ApplicationLifecycle` now owns every explicit `SceneTree.Quit` call.
Each request logs a stable reason and exit code. The root disables Godot's
automatic quit acceptance, handles `NOTIFICATION_WM_CLOSE_REQUEST`, and
labels that path `desktop-window-close`.

Interactive Escape uses one navigation routine for the early bootstrap and
the legacy unhandled-input path. World, selector, and source/picker surfaces
never fall through to process exit: they navigate back when possible and
otherwise log a `stay-alive` event. Smoke, capture, composition,
title-screen, and watchdog exits retain their existing exit codes but now carry
distinct lifecycle reasons. Joypad buttons feed gameplay input (or dismiss the
title screen), while `ui_cancel` routes to selector/map Back behavior; no
controller-specific path calls the lifecycle quit helper.

A root exit also records the last application-requested quit reason. A future
process disappearance without a matching `quit-request` is therefore outside
the explicit application-quit paths audited here and can be investigated as
an engine, OS/window-manager, runner, or other external teardown rather than
being silently conflated with ordinary navigation.

## Managed reproduction

Run-002672 repeated the ordinary direct Veldin launch shape under MjauRunner.
A PID-scoped Win32 probe targeted only that run's owned Godot game window.

1. The first Escape navigated Veldin to Worlds and logged
   `navigation reason=world-to-destinations`; the process remained alive.
2. A second Escape navigated Worlds to Game Sources and logged
   `navigation reason=selector-to-sources`; the process remained alive.
3. An explicit `WM_CLOSE` logged
   `quit-request reason=desktop-window-close exitCode=0`, followed by
   `root-exit ... requestedQuit=desktop-window-close`. MjauRunner then
   recorded a normal code-0 exit with no stop reason.

Run-002738 then held the same ordinary Veldin launch alive beyond the
approximately eight-minute lifetime seen in run-002233, with no stderr and no
lifecycle exit event. That rules out a fixed application or MjauRunner daemon
timeout at the old failure duration. An intentional third Escape after
navigating back to Game Sources exposed one remaining intermediate-build
Picker fallback; that classification was corrected before final verification.

Run-002777 verifies the final behavior. Three PID-scoped Escapes produced, in
order, `world-to-destinations`, `selector-to-sources`, and
`stay-alive reason=unhandled-escape mode=Picker scene=sources`; the process
remained alive after all three. A targeted `WM_CLOSE` then produced
`quit-request reason=desktop-window-close exitCode=0` and the matching root
exit before MjauRunner recorded a normal code-0 exit with no stop reason.

This establishes the intended distinction: gameplay/navigation input stays
inside the application, while an actual desktop close remains functional and
is now identifiable in the retained process log.
