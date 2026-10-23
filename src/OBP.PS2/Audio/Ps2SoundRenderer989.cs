using System.Buffers.Binary;

namespace OBP.PS2.Audio;

/// <summary>
/// Bounded SBlk v1/v3 tone/grain rendering: 240-Hz delays, branch selection,
/// pitch changes, key-off and SPU ADSR. Unsupported control programs remain
/// explicit. Interpolation, LFO waveforms and mixing are host approximations.
/// The 989snd reference implementation is acknowledged in THIRD_PARTY_NOTICES.
/// </summary>
public static class Ps2SoundRenderer989
{
    public const int SampleRate = 48_000, GrainRate = 240;
    public sealed record Rendered(short[] Samples, int LoopStartFrame, int LoopEndFrame);
    /// <summary>
    /// Independently scheduled voice. A looping voice retains its own pitch,
    /// intro and sample cycle; unrelated sustained voices must not share a loop.
    /// </summary>
    public sealed record ScheduledVoice(int StartFrame, bool Loop, Rendered Audio);
    private sealed record Voice(Ps2SoundBank989.Tone Tone, Ps2SpuAdpcm.DecodedSample Source, double Start, double Bend)
    {
        public double? Release;
        public double? Kill;
    }
    private sealed record Lfo(int Target, int Shape, int Depth, uint Step, int Start);
    private sealed record Program(List<Voice> Voices, List<Lfo> Lfos, double Tick, int PitchModifier = 0);

    public static Rendered? TryRender(Ps2SoundBank989.Sound sound, Func<Ps2SoundBank989.Tone, Ps2SpuAdpcm.DecodedSample> decode, int? seed = null)
    {
        var program = Interpret(sound, decode, seed);
        return program is null ? null : Render(sound, program);
    }

    /// <summary>
    /// Uses the same grain interpreter as TryRender, but returns separate voice
    /// clips for hosts capable of scheduling them. Time-varying program LFOs
    /// need a live synthesizer and are deliberately not admitted here.
    /// </summary>
    public static IReadOnlyList<ScheduledVoice>? TryRenderVoices(Ps2SoundBank989.Sound sound,
        Func<Ps2SoundBank989.Tone, Ps2SpuAdpcm.DecodedSample> decode, int? seed = null, int pitchModifier = 0)
    {
        if (pitchModifier is < short.MinValue or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(pitchModifier));
        var program = Interpret(sound, decode, seed, pitchModifier);
        if (program is null || program.Lfos.Count != 0) return null;
        var output = new List<ScheduledVoice>();
        foreach (var voice in program.Voices)
        {
            bool loop = voice.Source.Info.Repeats && voice.Release is null && voice.Kill is null;
            var local = new Voice(voice.Tone, voice.Source, 0, voice.Bend)
            {
                Release = voice.Release - voice.Start,
                Kill = voice.Kill - voice.Start,
            };
            var single = sound with { Flags = loop ? (ushort)1 : (ushort)0 };
            Rendered? audio = Render(single, new([local], [], Math.Max(0, program.Tick - voice.Start * GrainRate), program.PitchModifier));
            if (audio is null) return null;
            output.Add(new((int)Math.Round(voice.Start * SampleRate), loop, audio));
        }
        return output.AsReadOnly();
    }

    private static Program? Interpret(Ps2SoundBank989.Sound sound,
        Func<Ps2SoundBank989.Tone, Ps2SpuAdpcm.DecodedSample> decode, int? seed, int pitchModifier = 0)
    {
        var voices = new List<Voice>(); var lfos = new List<Lfo>();
        var random = new Random(seed ?? sound.Id); double tick = 0, bend = 0;
        var registers = new int[4];
        int pc = 0, operations = 0, skipAt = -1, skipTo = -1;
        while (pc < sound.Grains.Count)
        {
            if (++operations > 512 || tick > GrainRate * 8) return null;
            if (pc == skipAt) pc = skipTo;
            if (pc >= sound.Grains.Count) break;
            var grain = sound.Grains[pc]; tick += grain.Delay;
            var p = grain.Parameters ?? new byte[32];
            short Param(int at) => BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(at * 2));
            switch (grain.Type)
            {
                case 0: case 20: case 35: break;
                case 1:
                case 9:
                    if (grain.Tone is not { } tone || (tone.Flags & 8) != 0) return null;
                    if (tone.Volume < 0)
                    {
                        int volume = tone.Volume switch
                        {
                            >= -4 => registers[-tone.Volume - 1],
                            -5 => random.Next(127),
                            _ => -1, // Global block registers require their own caller-owned state.
                        };
                        if (volume < 0) return null;
                        tone = tone with { Volume = (sbyte)Math.Min(volume, 127) };
                    }
                    voices.Add(new(tone, decode(tone), tick / GrainRate, bend)); break;
                case 4:
                    if (p.Length < 16 || p[1] is not (0 or 1 or 2 or 3 or 4)) return null;
                    lfos.Add(new(p[1], p[3], BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(6)),
                        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(12)), BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(10)))); break;
                case 24: pc = sound.Grains.Count; continue;
                case 25:
                    int options = Param(0), count = Param(1);
                    if (options <= 0 || count <= 0 || pc + 1 + options * count > sound.Grains.Count) return null;
                    int selected = random.Next(options);
                    skipAt = pc + 1 + (selected + 1) * count; skipTo = pc + 1 + options * count;
                    pc += selected * count; break;
                case 26:
                    int maximum = BinaryPrimitives.ReadInt32LittleEndian(p);
                    if (maximum <= 0) return null;
                    tick += random.Next(maximum); break;
                case 27: bend = Param(0) * (random.NextDouble() * 2 - 1) / 100; break;
                case 28: bend = Math.Clamp(bend + Param(0) / 127d, -1, 1); break;
                case 29: bend = Param(0) / (Param(0) >= 0 ? 127d : 128d); break;
                case 30:
                case 31:
                case 32:
                case 33:
                    int register = Param(0);
                    if ((uint)register >= registers.Length) return null;
                    if (grain.Type == 30) registers[register] = Param(1);
                    if (grain.Type == 31)
                    {
                        if (Param(1) > Param(2)) return null;
                        registers[register] = random.Next(Param(1), Param(2) + 1);
                    }
                    if (grain.Type is 32 or 33) registers[register] = Math.Clamp(registers[register] + (grain.Type == 32 ? 1 : -1), -128, 127);
                    break;
                case 34:
                    int test = Param(0), action = Param(1), comparison = Param(2);
                    if ((uint)test >= registers.Length) return null;
                    if (action == 0 && registers[test] >= comparison || action == 1 && registers[test] != comparison ||
                        action >= 2 && comparison >= registers[test]) pc++;
                    break;
                case 36:
                    int marker = -1;
                    for (int index = 0; index < sound.Grains.Count; index++)
                        if (sound.Grains[index] is { Type: 35, Parameters: { } mark } &&
                            BinaryPrimitives.ReadInt16LittleEndian(mark) == Param(0)) { marker = index; break; }
                    if (marker < 0) return null;
                    pc = marker - 1; break;
                case 41:
                    foreach (var voice in voices) voice.Release ??= tick / GrainRate;
                    break;
                case 42:
                    foreach (var voice in voices) voice.Kill ??= tick / GrainRate;
                    break;
                default: return null;
            }
            pc++;
        }
        return new(voices, lfos, tick, pitchModifier);
    }

    private static Rendered? Render(Ps2SoundBank989.Sound sound, Program program)
    {
        var (voices, lfos, tick, pitchModifier) = program;
        if (voices.Count == 0) return new([], 0, 0); // A successfully interpreted, intentionally silent probability branch.
        double end = Math.Max(tick / GrainRate, voices.Max(voice => voice.Start +
            voice.Source.Samples.Length / ToneRate(voice.Tone, voice.Bend, pitchModifier: pitchModifier)));
        int loopStart = 0;
        var sustained = voices.LastOrDefault(voice => voice.Release is null && voice.Kill is null && voice.Source.Info.Repeats);
        if (sound.Loop && sustained is not null)
        {
            var info = sustained.Source.Info; double rate = ToneRate(sustained.Tone, sustained.Bend, pitchModifier: pitchModifier);
            double cycle = (info.PcmSampleCount - (info.LoopStartSample ?? 0)) / rate;
            double start = sustained.Start + (info.LoopStartSample ?? 0) / rate;
            double stable = Math.Max(tick / GrainRate + .25, start + .25);
            start += Math.Ceiling((stable - start) / cycle) * cycle;
            loopStart = (int)Math.Round(start * SampleRate); end = start + cycle;
        }
        int length = Math.Clamp((int)Math.Ceiling((end + (sound.Loop ? 0 : .3)) * SampleRate), 1, SampleRate * 8);
        if (loopStart >= length) return null;
        var mix = new double[length];
        foreach (var voice in voices)
        {
            int start = (int)Math.Round(voice.Start * SampleRate);
            double cursor = 0; var envelope = new Envelope(voice.Tone.Adsr1, voice.Tone.Adsr2);
            bool released = false; var pcm = voice.Source.Samples; var info = voice.Source.Info;
            for (int frame = start; frame < length; frame++)
            {
                double time = (double)frame / SampleRate;
                if (voice.Kill is { } kill && time >= kill) break;
                if (!released && voice.Release is { } release && time >= release) { envelope.Release(); released = true; }
                double level = envelope.Next(); if (envelope.Stopped) break;
                if (cursor >= pcm.Length)
                {
                    if (!info.Repeats) break;
                    int loop = info.LoopStartSample ?? 0;
                    cursor = loop + (cursor - loop) % (pcm.Length - loop);
                }
                int index = (int)cursor; double fraction = cursor - index;
                double value = pcm[index] + (pcm[Math.Min(index + 1, pcm.Length - 1)] - pcm[index]) * fraction;
                double modulation = 0, volume = 1, currentBend = voice.Bend;
                foreach (var lfo in lfos)
                {
                    double phase = (time * GrainRate * lfo.Step / 65536 + lfo.Start) / 2048;
                    double wave = lfo.Shape switch
                    {
                        1 => Math.Sin(phase * Math.Tau),
                        2 => phase % 1 < .5 ? 1 : -1,
                        3 => 1 - 4 * Math.Abs(phase % 1 - .5),
                        4 => phase % 1 * 2 - 1,
                        _ => 0,
                    };
                    if (lfo.Target == 1) volume *= Math.Clamp(1 + (wave - 1) * lfo.Depth / 2048d, 0, 1);
                    if (lfo.Target == 3) modulation += wave * 6096 * lfo.Depth / 1024d;
                    if (lfo.Target == 4) currentBend += wave * lfo.Depth / 1024d;
                }
                mix[frame] += value * level * volume * voice.Tone.Volume / 127d * sound.Volume / 127d;
                cursor += ToneRate(voice.Tone, currentBend, modulation, pitchModifier) / SampleRate;
            }
        }
        var result = mix.Select(sample => (short)Math.Clamp((int)Math.Round(sample), short.MinValue, short.MaxValue)).ToArray();
        return new(result, loopStart, result.Length);
    }

    private static double ToneRate(Ps2SoundBank989.Tone tone, double bend, double modulation = 0, int pitchModifier = 0)
    {
        double semitones = bend >= 0 ? bend * tone.PitchBendHigh : bend * Math.Abs((int)tone.PitchBendLow);
        return (tone.CenterNote < 0 ? 48_000 : 44_100) * Math.Pow(2,
            (60 - Math.Abs((int)tone.CenterNote) + (tone.CenterFine + modulation + pitchModifier) / 128d + semitones) / 12);
    }

    // SPU envelope arithmetic follows the 989snd reference's ADSR fields and
    // sample-rate counter. It is separate from ADPCM/sample-loop interpretation.
    private sealed class Envelope(ushort a, ushort b)
    {
        private int _phase, _level, _counter;
        public bool Stopped => _phase == 4;
        public void Release() { _phase = 3; _counter = 0; }
        public double Next()
        {
            bool decreasing = _phase is 1 or 3 || _phase == 2 && (b & 0x4000) != 0;
            bool exponential = _phase switch { 0 => (a & 0x8000) != 0, 1 => true, 2 => (b & 0x8000) != 0, _ => (b & 0x20) != 0 };
            int shift = _phase switch { 0 => (a >> 10) & 31, 1 => (a >> 4) & 15, 2 => (b >> 8) & 31, _ => b & 31 };
            int step = _phase switch { 0 => 7 - ((a >> 8) & 3), 1 or 3 => -8, _ => decreasing ? -8 + ((b >> 6) & 3) : 7 - ((b >> 6) & 3) };
            int clock = 0x800000 >> Math.Max(0, shift - 11);
            step <<= Math.Max(0, 11 - shift);
            if (exponential)
            {
                if (decreasing) step = step * _level >> 15;
                else if (_level > 0x6000) clock >>= 2;
            }
            _counter += clock;
            if (_counter >= 0x800000) { _counter = 0; _level = Math.Clamp(_level + step, 0, 32767); }
            if (_phase == 0 && _level == 32767) _phase = 1;
            else if (_phase == 1 && _level <= ((a & 15) + 1) * 2048) _phase = 2;
            else if (_phase == 3 && _level == 0) _phase = 4;
            return _level / 32767d;
        }
    }
}
