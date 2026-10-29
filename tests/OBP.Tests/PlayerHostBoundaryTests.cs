using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OBP.Tests;

/// <summary>
/// Reviewed guard for responsibilities that cannot grow in PlayerHost. It does
/// not forbid new physics probes, scene geometry, or camera adapter methods.
/// </summary>
public sealed class PlayerHostBoundaryTests
{
    private static readonly string Scripts = Path.Combine(RepoPaths.Root, "game", "scripts");

    [Fact]
    public void RealPlayerHostPartialsHaveNoUnownedGameplayOrScenarioImplementations()
    {
        // Include newly introduced partials even if they are not named
        // PlayerHost.*; the class declaration, not the filename, is guarded.
        var errors = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "game"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                           !path.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => Violations(Path.GetRelativePath(RepoPaths.Root, path), File.ReadAllText(path)));
        Assert.Empty(errors);
    }

    [Fact]
    public void PlayerHostLedgesDelegateRegrabAndJumpPressTimingInOrder()
    {
        string ledges = File.ReadAllText(Path.Combine(Scripts, "PlayerHost.Rac1Ledges.cs"));
        int advance = ledges.IndexOf("_rac1Ledge.AdvanceInput(jumpPressed)", StringComparison.Ordinal);
        int eligibility = ledges.IndexOf("_rac1Ledge.CanProbeForGrab", StringComparison.Ordinal);
        int step = ledges.IndexOf("_rac1Ledge.Step(", StringComparison.Ordinal);
        Assert.True(advance >= 0 && eligibility > advance && step > eligibility,
            "Ledge input must advance before the grab check and traversal step.");
        Assert.DoesNotContain("_rac1LedgeRegrabDelay", ledges, StringComparison.Ordinal);
        Assert.DoesNotContain("_rac1LedgeJumpPressAge", ledges, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private int _campaignProgress;")]
    [InlineData("public int QuestProgress { get; set; }")]
    [InlineData("private Dictionary<int, int> _actorStates = new();")]
    [InlineData("private Dictionary<int, int> ActorStates { get; } = new();")]
    [InlineData("private List<int> ActorStates { get; init; } = new();")]
    [InlineData("private int CampaignProgress { get; private set; }")]
    [InlineData("private Queue<Rac1ActorEvent> _pending = new();")]
    [InlineData("private Stack<Rac1EnemyState> _pending = new();")]
    [InlineData("private System.Collections.Immutable.ImmutableArray<Rac1ActorEvent> _pending;")]
    [InlineData("private Rac1ActorEvent[] _pending = [];")]
    [InlineData("private Godot.Vector3[] _campaignPositions = [];")]
    [InlineData("private Rac1WeaponInventory _newInventory;")]
    [InlineData("private static float RecoveredRac1Jump(float speed) => speed * 1.5f;")]
    [InlineData("private double CalculateDamage(double value) => value / 2;")]
    [InlineData("private static float RecoveredRac1Jump(float speed) { speed *= 1.5f; return speed; }")]
    [InlineData("private static float NativeDamage(Rac1DamageFacts facts) => facts.Damage * 0.5f;")]
    [InlineData("private double CalculateNativeRate(double value) => Math.Sqrt(value);")]
    [InlineData("private void RunRac1SmokeScenario() { }")]
    [InlineData("private void OrchestrateDiagnosticFixture() { }")]
    public void NewNativeRulesGlobalStateAndScenariosAreRejected(string member)
    {
        string source = "namespace OBP.Game; public partial class PlayerHost { " + member + " }";
        Assert.NotEmpty(Violations("PlayerHost.New.cs", source));
    }

    [Theory]
    [InlineData("private bool ProbeHostCollision() { var ray = Godot.PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition + Godot.Vector3.Down); return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count != 0; }")]
    [InlineData("private static Godot.Vector3 SceneNormal(Godot.Vector3 value) => value.Normalized();")]
    [InlineData("private int _cameraProbeCount;")]
    [InlineData("private Godot.Vector3 _lastSlideNormal;")]
    [InlineData("private Godot.Vector3[] _lastSlideNormals = [];")]
    [InlineData("private Queue<Godot.Vector3> _collisionQueue = new();")]
    [InlineData("private int GroundedTicks => 10;")]
    public void GodotContactCameraAndTransientProfilingRemainPermitted(string member)
    {
        string source = "namespace OBP.Game; public partial class PlayerHost { " + member + " }";
        Assert.Empty(Violations("PlayerHost.New.cs", source));
    }

    private static readonly HashSet<string> ReviewedHostBridges = new(StringComparer.Ordinal)
    {
        // These are ephemeral snapshots, input events or scene-level selection
        // projections. Inventory, health and campaign authority live elsewhere.
        "Rac1GameplayState", "PlayerHealth", "Rac1MovementEquipment",
        "Rac1ActiveBackpack", "Rac1ClankAvailable", "Rac1SwingshotEquipped",
        "Rac1EquipmentLungeVelocity", "Rac1EquipmentActionActive",
        // Scene-lifetime authored swingshot candidate caches, not owned inventory.
        "_rac1TetherTargets", "_rac1SwingTargets",
    };

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex ForeignState = new(
        @"(?:^|_)(?:campaign|inventory|progress|quest|checkpoint|savegame|persist|actor|enemy|ammo|health|equipment|weapon)|(?:Campaign|Inventory|Progress|Quest|Checkpoint|Savegame|Persist|Actor|Enemy|Ammo|Health|Equipment|Weapon)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ScenarioName = new(
        "scenario|fixture|smoke|benchmark|orchestrat",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);

    private static readonly HashSet<string> NumericTypes =
        new(["int", "long", "short", "float", "double", "decimal", "bool"], StringComparer.Ordinal);

    private static IReadOnlyList<string> Violations(string file, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var errors = new List<string>();
        foreach (var host in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                     .Where(type => type.Identifier.ValueText == "PlayerHost"))
        {
            foreach (var field in host.Members.OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                    CheckState(variable.Identifier.ValueText, field.Declaration.Type.ToString(), file, errors);
            }
            foreach (var property in host.Members.OfType<PropertyDeclarationSyntax>()
                         .Where(property => property.AccessorList?.Accessors.Any(accessor =>
                             accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                             accessor.IsKind(SyntaxKind.InitAccessorDeclaration) ||
                             (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) &&
                              accessor.Body is null && accessor.ExpressionBody is null)) == true))
                CheckState(property.Identifier.ValueText, property.Type.ToString(), file, errors);

            foreach (var method in host.Members.OfType<MethodDeclarationSyntax>())
            {
                string name = method.Identifier.ValueText;
                if (ScenarioName.IsMatch(name))
                    errors.Add($"{file}::{name}: scenario orchestration belongs to diagnostic fixtures/services");
                // A standalone primitive arithmetic recurrence has no reason
                // to be implemented by the Godot body. Numeric space conversions
                // involving Godot vectors/collisions are deliberately not caught.
                bool pureNumericSignature = NumericTypes.Contains(method.ReturnType.ToString()) &&
                    method.ParameterList.Parameters.All(parameter =>
                        parameter.Type is not null &&
                        (NumericTypes.Contains(parameter.Type.ToString()) ||
                         parameter.Type.ToString().StartsWith("Rac1", StringComparison.Ordinal)));
                bool formula = method.DescendantNodes().OfType<BinaryExpressionSyntax>().Any(expression =>
                    expression.IsKind(SyntaxKind.AddExpression) ||
                    expression.IsKind(SyntaxKind.SubtractExpression) ||
                    expression.IsKind(SyntaxKind.MultiplyExpression) ||
                    expression.IsKind(SyntaxKind.DivideExpression) ||
                    expression.IsKind(SyntaxKind.ModuloExpression)) ||
                    method.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                        assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
                        assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) ||
                        assignment.IsKind(SyntaxKind.MultiplyAssignmentExpression) ||
                        assignment.IsKind(SyntaxKind.DivideAssignmentExpression) ||
                        assignment.IsKind(SyntaxKind.ModuloAssignmentExpression)) ||
                    method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                        call.Expression.ToString().StartsWith("Math.", StringComparison.Ordinal) ||
                        call.Expression.ToString().StartsWith("MathF.", StringComparison.Ordinal));
                bool godotInput = method.DescendantTokens().Any(token => token.ValueText is
                    "GlobalPosition" or "Velocity" or "MoveAndSlide" or "GetWorld3D" or "GetRid" or
                    "IsOnFloor" or "PhysicsServer3D" or "PhysicsRayQueryParameters3D" or
                    "Vector3" or "Vector2" or "Transform3D" or "Camera");
                if (pureNumericSignature && formula && !godotInput)
                    errors.Add($"{file}::{name}: source-native numeric gameplay formula belongs to a source/session owner");
            }
        }
        return errors;
    }

    // Review the element rather than rejecting every engine-side queue or array.
    // A queue of collision normals is a host adapter; a queue of actor events
    // would silently make PlayerHost a gameplay-state owner.
    private static bool IsForeignStateCollection(string type)
    {
        TypeSyntax parsed = SyntaxFactory.ParseTypeName(type);
        return parsed.DescendantNodesAndSelf().OfType<ArrayTypeSyntax>()
                   .Any(array => ForeignState.IsMatch(array.ElementType.ToString())) ||
               parsed.DescendantNodesAndSelf().OfType<GenericNameSyntax>()
                   .Any(generic => generic.Identifier.ValueText is "Queue" or "Stack" or "ImmutableArray" &&
                                   generic.TypeArgumentList.Arguments.Any(argument =>
                                       ForeignState.IsMatch(argument.ToString())));
    }

    private static void CheckState(string name, string type, string file, List<string> errors)
    {
        if (ReviewedHostBridges.Contains(name))
            return;
        if (ForeignState.IsMatch(name) ||
            ForeignState.IsMatch(type) && (type.Contains("Inventory", StringComparison.OrdinalIgnoreCase) ||
                                            type.Contains("Campaign", StringComparison.OrdinalIgnoreCase)) ||
            type.Contains("Dictionary<", StringComparison.Ordinal) ||
            type.Contains("HashSet<", StringComparison.Ordinal) ||
            type.Contains("List<", StringComparison.Ordinal) ||
            IsForeignStateCollection(type))
            errors.Add($"{file}::{name}: durable player/actor/equipment state belongs to a session or world owner");
    }
}
