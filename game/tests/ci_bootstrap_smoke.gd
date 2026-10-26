extends SceneTree

# A deliberately asset-free integration check of the actual OBPGame scene.
# Run: godot --headless --path game --script res://tests/ci_bootstrap_smoke.gd -- --test-scene picker

func _initialize() -> void:
    call_deferred("_run_smoke")


func _run_smoke() -> void:
    # The CI workflow imports resources before this smoke. That import must not
    # produce any uncommitted C# UID sidecars or modify checked-in resources.
    if not _verify_clean_godot_import():
        return

    var scene: PackedScene = load("res://scenes/OBPGame.tscn") as PackedScene
    if scene == null:
        _fail("Main scene does not load as a PackedScene")
        return

    var game: Node = scene.instantiate()
    if game == null:
        _fail("Main scene cannot be instantiated")
        return

    if not game is Node3D or game.get_node_or_null("SourceManagerBootstrap") == null:
        game.free()
        _fail("Main scene is missing OBPGame root or SourceManagerBootstrap")
        return

    root.add_child(game)
    # The standalone scenario owns the same six-frame settling/assertion through
    # copied scene facts and the bounded diagnostic clock contract.
    var probe: Node = load("res://diagnostics/SourceFreeStartupProbe.cs").new()
    root.add_child(probe)
    # Connect the await before Start can synchronously reject an invalid launch.
    probe.call_deferred("Start", game)
    var result: Array = await probe.Completed
    probe.queue_free()
    if not result[0]:
        _fail(str(result[1]))
        return

    print("[godot-ci-smoke] PASS: OBPGame loads and Game Sources opens without retail data")
    game.queue_free()
    for frame in range(3):
        await process_frame
    root.add_child(load("res://diagnostics/BugReportMenuSmoke.cs").new())


func _verify_clean_godot_import() -> bool:
    var repo_root: String = ProjectSettings.globalize_path("res://").trim_suffix("/").get_base_dir()
    var output: Array = []
    var exit_code: int = OS.execute(
        "git",
        PackedStringArray(["-C", repo_root, "status", "--porcelain", "--untracked-files=all", "--", "game"]),
        output,
        true
    )
    if exit_code != 0:
        _fail("Cannot verify clean Godot import (git status exit %d): %s" % [exit_code, str(output)])
        return false
    var changes: String = ""
    for line in output:
        changes += str(line)
    if not changes.strip_edges().is_empty():
        _fail("Godot import modified game/ files. Sync and commit C# UIDs first: " + changes)
        return false
    return true


func _fail(message: String) -> void:
    push_error("[godot-ci-smoke] FAIL: " + message)
    quit(1)
