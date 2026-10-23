extends SceneTree

# Asset-free regression using the actual inbox and normal input events.
func _initialize() -> void:
    call_deferred("_run")


func _run() -> void:
    root.add_child(load("res://diagnostics/BugReportMenuSmoke.cs").new())
