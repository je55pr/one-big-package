using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Bounded static-world pickup motion from loaded Oozla 0x376D58/0x378270
/// and reward emitter 0x3177C8. Vectors are native Z-up, per 60 Hz tick.
/// The host supplies contact, player attraction dimensions, target socket and
/// random samples. Moving supports, camera-side steering and sparkles remain
/// unavailable; host randomness does not reproduce the retail global call order.
/// </summary>
public sealed class Rac2BoltMotionSession
{
    public static float GravityPerTick => .003f;
    public static float ContactRadius => .25f;
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public int NativeState { get; private set; } = 1;
    public bool Collected { get; private set; }
    public float Spin { get; private set; }
    private readonly Func<float, float, float> _sample;
    private readonly float _launchHeight, _spinStep;
    private Vector3 _rest;
    private int _hopLength, _hopRemaining;
    private float _acceleration;
    private bool _activated;

    public Rac2BoltMotionSession(Vector3 origin, Func<float, float, float> sample)
    {
        _sample = sample;
        float angle = sample(-MathF.PI, MathF.PI);
        Velocity = new(MathF.Cos(angle) * sample(0, 3) / 60, MathF.Sin(angle) * sample(0, 3) / 60,
            sample(3.7f, 6) / 60);
        Vector3 offset = new(Velocity.X, Velocity.Y, 0);
        if (offset.LengthSquared() > 0) offset = Vector3.Normalize(offset) * .5f;
        Position = origin + offset;
        _launchHeight = Position.Z;
        _spinStep = sample(0, 720) * MathF.PI / 180 / 60;
    }

    public void Step(Vector3 player, Vector3 socket, float radius, float height, bool attractEnabled) =>
        Step(player, socket, radius, height, attractEnabled, null);

    public void Step(Vector3 player, Vector3 socket, float radius, float height, bool attractEnabled,
        Rac2BoltContact? contact)
    {
        if (Collected) return;
        var delta = player - Position;
        bool nearby = delta.X * delta.X + delta.Y * delta.Y < radius * radius && MathF.Abs(delta.Z) < height;
        if (attractEnabled && nearby) _activated = true;
        Spin += _spinStep;
        switch (NativeState)
        {
            case 1: StepFlight(player, attractEnabled, contact); break;
            case 2: StepFirstHop(); break;
            case 3:
                if (attractEnabled && _activated) Attract();
                else Hop(.0125f);
                break;
            case 4 when attractEnabled: StepAttracted(socket); break;
        }
    }

    private void StepFlight(Vector3 player, bool attractEnabled, Rac2BoltContact? contact)
    {
        Velocity -= new Vector3(0, 0, GravityPerTick);
        Position += Velocity;
        if (Position.Z < _launchHeight - 2 && Position.Z < player.Z - 2 && attractEnabled)
            Attract();
        else if (contact is { } hit && Velocity.Z < 0 && hit.Normal.Z > .70710677f)
        {
            _rest = hit.Position + Vector3.UnitZ * ContactRadius;
            Position = _rest;
            if (attractEnabled && _activated) { Velocity *= .13333334f; Attract(); }
            else
            {
                NativeState = 2;
                _hopLength = _hopRemaining = (int)_sample(30, 40);
                Velocity = Vector3.Zero;
            }
        }
    }

    private void StepFirstHop()
    {
        Hop(.025f);
        if (_hopRemaining == 0)
        {
            NativeState = 3;
            _hopLength = _hopRemaining = (int)_sample(10, 20);
        }
    }

    private void StepAttracted(Vector3 socket)
    {
        // Front-of-camera attraction branch. Contact/socket mapping is
        // supplied by the host; no borrowed world/player controller.
        float speed = Velocity.Length();
        if (speed > .006666667f) Velocity *= (speed - .006666667f) / speed;
        _acceleration = Math.Min(.53333336f, _acceleration + .006666667f);
        Position += Velocity;
        var to = socket - Position;
        if (to.Length() <= _acceleration) { Position = socket; Collected = true; }
        else Position += Vector3.Normalize(to) * _acceleration;
    }

    private void Attract() { NativeState = 4; _acceleration = 0; }
    private void Hop(float amplitude)
    {
        Position = _rest;
        if (_hopRemaining <= 0) return;
        float t = (float)_hopRemaining / _hopLength - .5f;
        Position += Vector3.UnitZ * ((.25f - t * t) * amplitude * _hopLength);
        _hopRemaining--;
    }
}

public readonly record struct Rac2BoltContact(Vector3 Position, Vector3 Normal);
