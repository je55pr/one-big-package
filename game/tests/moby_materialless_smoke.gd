extends SceneTree

# Run with the pinned Godot .NET:
# godot --headless --path game --script res://tests/moby_materialless_smoke.gd

func _initialize() -> void:
    call_deferred("_run")


func _run() -> void:
    var script: Script = load("res://tests/MobyMateriallessProbe.cs")
    if script == null or not script.can_instantiate():
        push_error("[moby-materialless-smoke] C# fixture unavailable; build game in Debug")
        quit(1)
        return
    root.add_child(script.new())
