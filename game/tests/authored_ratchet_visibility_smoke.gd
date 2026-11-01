extends SceneTree

# Run with pinned Godot .NET after building the game.
# godot --headless --path game --script res://tests/authored_ratchet_visibility_smoke.gd

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var script: Script = load("res://tests/AuthoredRatchetVisibilityProbe.cs")
    if script == null or not script.can_instantiate():
        push_error("[authored-ratchet-visibility-smoke] Fixture unavailable; build the Godot C# game first")
        quit(1)
        return
    root.add_child(script.new())
