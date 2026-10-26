extends SceneTree

# Run with pinned Godot .NET:
# godot --headless --path game --script res://tests/ambient_uv_scroll_smoke.gd

func _initialize() -> void:
    call_deferred("_run")


func _run() -> void:
    var script: Script = load("res://tests/AmbientUvScrollProbe.cs")
    if script == null:
        push_error("[ambient-uv-scroll-smoke] C# fixture script did not load")
        quit(1)
        return
    if not script.can_instantiate():
        push_error("[ambient-uv-scroll-smoke] C# fixture is not instantiable; build the game in Debug first")
        quit(1)
        return
    root.add_child(script.new())
