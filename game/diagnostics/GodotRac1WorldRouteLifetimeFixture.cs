using Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotRac1WorldRouteLifetimeFixture(Node owner, Func<Rac1WorldRouteResources> read,
    Func<Node3D?> root, Func<IEnumerable<Node>> hulls, Func<Camera3D?> camera) : IRac1WorldRouteLifetimeFixture
{
    public Rac1WorldRouteResources Read() => read();
    public IRac1WorldRouteRetirementWitness Capture() => new Witness(root(), hulls().ToArray(), owner.GetChildren()
        .Where(node => node.Name.ToString().StartsWith("RuntimeAudio", StringComparison.Ordinal)).ToArray(), camera());
    public IRac1WorldRouteDetachedProbe CreateDetachedProbe() => new DetachedProbe();
    private sealed class Witness(Node3D? root, Node[] hulls, Node[] audio, Camera3D? camera) : IRac1WorldRouteRetirementWitness
    {
        private static bool Live(Node? node) => GodotObject.IsInstanceValid(node);
        public Rac1WorldRouteRetirement Read() => new(!Live(root), hulls.Count(Live), audio.Count(Live), !Live(camera));
    }
    private sealed class DetachedProbe : IRac1WorldRouteDetachedProbe
    {
        private readonly Node3D _scene = new();
        private readonly StaticBody3D _hull = new();
        private readonly AudioStreamPlayer _audio = new();
        private readonly Camera3D _camera = new();
        public IRac1WorldRouteRetirementWitness Witness => new Witness(_scene, [_hull], [_audio], _camera);
        public void Dispose()
        {
            foreach (var node in new Node[] { _scene, _hull, _audio, _camera })
                if (GodotObject.IsInstanceValid(node)) node.Free();
        }
    }
}
