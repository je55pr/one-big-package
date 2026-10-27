extends SceneTree

var game: Node
var finished := false
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
	if finished or not is_instance_valid(game):
		return
	var world = game.get_node_or_null("WorldRoot")
	if world == null or world.get_child_count() == 0 or game.find_child("PlayerHost", true, false) == null:
		return
	finished = true
	if game.find_child("FusionLabStatus", true, false) != null:
		push_error("[lab-presentation-check] ordinary Lab showed a development status caption")
		quit(1)
		return
	for label in world.find_children("*", "Label3D", true, false):
		if "DEVELOPER" in label.text or "PLAYTEST" in label.text or "Diagnostic capsule" in label.text:
			push_error("[lab-presentation-check] ordinary Lab showed a development sign")
			quit(1)
			return
		if label.text.ends_with("\n"):
			push_error("[lab-presentation-check] ordinary Lab sign has a trailing blank line")
			quit(1)
			return
	print("[lab-presentation-check] PASS: ordinary playable Lab has no development captions")
	game.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)

func _timeout() -> void:
	push_error("[lab-presentation-check] timed out waiting for ordinary Lab entry/shutdown")
	quit(2)
