using Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1WorldRouteLifetimeFixture(Node owner, Func<Rac1WorldRouteResources> read,
    Func<Node3D?> root, Func<IEnumerable<Node>> hulls, Func<Camera3D?> camera) : IRac1WorldRouteLifetimeFixture
{
    public Rac1WorldRouteResources Read() => read();
    public IRac1WorldRouteRetirementWitness Capture() => new Witness(root(), hulls().ToArray(), owner.GetChildren()
        .Where(node => node.Name.ToString().StartsWith("RuntimeAudio", StringComparison.Ordinal)).ToArray(), camera());
    private sealed class Witness(Node3D? root, Node[] hulls, Node[] audio, Camera3D? camera) : IRac1WorldRouteRetirementWitness
    {
        private static bool Live(Node? node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree();
        public Rac1WorldRouteRetirement Read() => new(!Live(root), hulls.Count(Live), audio.Count(Live), !Live(camera));
    }
}
