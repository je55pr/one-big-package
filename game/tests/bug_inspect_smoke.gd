extends SceneTree

# Run with --replay-bug-report / --bug-report-id / --playtest-inspect-marker.
# Verify the interactive replay retains the live scene at its marked moment.
func _initialize() -> void:
    call_deferred("_run")


func _run() -> void:
    var args: PackedStringArray = OS.get_cmdline_user_args()
    var index: int = args.find("--playtest-report")
    if index < 0 or index + 1 >= args.size():
        _fail("Missing result path")
        return
    var result_path: String = args[index + 1]
    var scene: PackedScene = load("res://scenes/OBPGame.tscn") as PackedScene
    var game: Node = scene.instantiate()
    root.add_child(game)
    var deadline: int = Time.get_ticks_msec() + 60000
    while not FileAccess.file_exists(result_path) and Time.get_ticks_msec() < deadline:
        await process_frame
    if not FileAccess.file_exists(result_path):
        _fail("Interactive replay produced no result")
        return
    var result: Variant = JSON.parse_string(FileAccess.get_file_as_string(result_path))
    if not result is Dictionary or result.get("Result") != "matched" or not paused:
        _fail("Replay did not match and pause at the marker")
        return
    for frame in range(5):
        await process_frame
    if not paused or not is_instance_valid(game) or not game.is_inside_tree():
        _fail("Marked scene was released instead of retained for inspection")
        return
    print("[bug-inspect-smoke] PASS: matched marker, paused live scene, normal window-close teardown")
    game.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)


func _fail(message: String) -> void:
    push_error("[bug-inspect-smoke] FAIL: " + message)
    quit(1)
