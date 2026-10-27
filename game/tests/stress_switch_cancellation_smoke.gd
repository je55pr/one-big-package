extends SceneTree

var game: Node
var frames_after_entry := 0
var canceled := false
var deadline := 0

func _initialize() -> void:
	call_deferred("_start")

func _start() -> void:
	game = load(ProjectSettings.get_setting("application/run/main_scene")).instantiate()
	root.add_child(game)
	deadline = Time.get_ticks_msec() + 90000
	process_frame.connect(_check)

func _check() -> void:
	if Time.get_ticks_msec() >= deadline:
		_timeout()
		return
	if canceled or not is_instance_valid(game):
		return
	var world = game.get_node_or_null("WorldRoot")
	if world == null or world.get_child_count() == 0 or game.find_child("PlayerHost", true, false) == null:
		return
	frames_after_entry += 1
	if frames_after_entry == 3:
		canceled = true
		print("[stress-cancellation-smoke] requesting close during the first world settle wait")
		game.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)

func _timeout() -> void:
	push_error("[stress-cancellation-smoke] timed out waiting for entry/shutdown")
	quit(2)
