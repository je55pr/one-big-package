extends SceneTree

# A deliberately asset-free integration check of the actual OBPGame scene.
# Run: godot --headless --path game --script res://tests/ci_bootstrap_smoke.gd -- --test-scene picker

func _initialize() -> void:
    call_deferred("_run_smoke")


func _run_smoke() -> void:
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
    # SourceManagerBootstrap routes to the Game Sources screen via CallDeferred.
    for frame in range(6):
        await process_frame

    var ui: Node = game.get_node_or_null("UI")
    var manager: Node = game.get_node_or_null("SourceManager")
    if ui == null or manager == null or not manager.is_inside_tree():
        _fail("Asset-free startup failed to display the Game Sources UI")
        return

    print("[godot-ci-smoke] PASS: OBPGame loads and Game Sources opens without retail data")
    game.queue_free()
    for frame in range(3):
        await process_frame
    root.add_child(load("res://diagnostics/BugReportMenuSmoke.cs").new())


func _fail(message: String) -> void:
    push_error("[godot-ci-smoke] FAIL: " + message)
    quit(1)
