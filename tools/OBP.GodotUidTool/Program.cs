using OBP.GodotUidTool;

try
{
    return GodotUidCommand.Run(args, Console.Out, Console.Error);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Godot UID sync failed: {ex.Message}");
    return 1;
}
