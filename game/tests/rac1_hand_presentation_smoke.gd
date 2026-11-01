extends SceneTree

# Run with pinned .NET Godot after building the C# game.
func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var script: Script = load("res://tests/Rac1HandPresentationProbe.cs")
    if script == null or not script.can_instantiate():
        push_error("[rac1-hand-presentation-smoke] Godot C# probe unavailable")
        quit(1)
        return
    root.add_child(script.new())
