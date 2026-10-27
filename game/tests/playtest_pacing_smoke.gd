extends SceneTree

# Observe public artifacts and use ordinary F9/window-close APIs, without private state.
func _initialize() -> void:
    call_deferred("_run_smoke")


func _run_smoke() -> void:
    var arguments: PackedStringArray = OS.get_cmdline_user_args()
    var recording: String = ""
    for index in range(arguments.size() - 1):
        if arguments[index] == "--record-playtest":
            recording = arguments[index + 1]
            break
    if recording.is_empty():
        _fail("A recording output is required")
        return

    var scene: PackedScene = load("res://scenes/OBPGame.tscn") as PackedScene
    if scene == null:
        _fail("Main scene could not be loaded")
        return
    var game: Node = scene.instantiate()
    root.add_child(game)

    var deadline: int = Time.get_ticks_msec() + 90000
    while not FileAccess.file_exists(recording + ".partial"):
        if Time.get_ticks_msec() >= deadline:
            _fail("Recording did not begin after source preparation")
            return
        await process_frame

    # Keep the real human-recorder pacing enabled. Count only after its writer
    # opens; bootstrap process frames are intentionally unpaced.
    for frame in range(180):
        await process_frame
    print("[playtest-pacing-smoke] completed 180 process frames after recording began")
    var save: InputEventKey = InputEventKey.new()
    save.physical_keycode = KEY_F9
    save.pressed = true
    Input.parse_input_event(save)
    deadline = Time.get_ticks_msec() + 90000
    while not FileAccess.file_exists(recording):
        if Time.get_ticks_msec() >= deadline:
            save.pressed = false
            Input.parse_input_event(save)
            _fail("Recording did not finish after ordinary F9 input")
            return
        await process_frame
    save.pressed = false
    Input.parse_input_event(save)
    game.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)
    deadline = Time.get_ticks_msec() + 60000
    while Time.get_ticks_msec() < deadline:
        await process_frame
    _fail("Game did not quit after window-close notification")


func _fail(message: String) -> void:
    push_error("[playtest-pacing-smoke] FAIL: " + message)
    quit(1)
