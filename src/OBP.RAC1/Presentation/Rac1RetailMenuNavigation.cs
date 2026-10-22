namespace OBP.RAC1.Presentation;

/// <summary>Menu display navigation only. It never changes inventory or campaign state.</summary>
public enum Rac1RetailMenuPage { Root, Weapons, Gadgets, QuickSelect, Items, Help, Options, Development }

public sealed class Rac1RetailMenuNavigation
{
    public const int TransitionTicks = 12;
    public Rac1RetailMenuPage Page { get; private set; } = Rac1RetailMenuPage.Root;
    public Rac1RetailMenuPage Destination { get; private set; } = Rac1RetailMenuPage.Root;
    public int RemainingTicks { get; private set; }
    public int SelectedIndex { get; private set; }
    public bool IsTransitioning => RemainingTicks > 0;

    private readonly Dictionary<Rac1RetailMenuPage, int> _focus = [];

    public bool Navigate(Rac1RetailMenuPage target)
    {
        if (IsTransitioning || target == Page) return false;
        Destination = target;
        RemainingTicks = TransitionTicks;
        return true;
    }

    public void Step(int ticks = 1)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (!IsTransitioning || ticks == 0) return;
        RemainingTicks = Math.Max(0, RemainingTicks - ticks);
        if (RemainingTicks != 0) return;
        _focus[Page] = SelectedIndex;
        Page = Destination;
        SelectedIndex = _focus.GetValueOrDefault(Page);
    }

    public void Focus(int index, int count)
    {
        if (IsTransitioning) return;
        SelectedIndex = count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
    }

    public bool Back() => Navigate(Rac1RetailMenuPage.Root);

    public static bool MayEquip(bool owned) => owned;
}

/// <summary>
/// Render-only HelpDesk state progression: the native display uses seven
/// stages, although its full event/audio gates are not hosted yet.
/// Inputs here are explicit requests from a UI owner, not gameplay polling.
/// </summary>
public sealed class Rac1HelpDeskPresentation
{
    public enum Phase { Hidden, Opening, Prompt, Expanding, FadeIn, Text, FadeOut, Closing }
    public Phase Current { get; private set; } = Phase.Hidden;
    public string Message { get; private set; } = string.Empty;
    public float Opacity { get; private set; }
    public float Expansion { get; private set; }
    private int _ticks;
    private string? _queued;

    public void ShowPrompt(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { Dismiss(); return; }
        if (Current != Phase.Hidden && Message == message) return;
        if (Current != Phase.Hidden)
        {
            _queued = message;
            Dismiss();
            return;
        }
        Message = message;
        _ticks = 0;
        Current = Phase.Opening;
    }

    public void Expand()
    {
        if (Current is Phase.Prompt or Phase.Opening)
        {
            Current = Phase.Expanding;
            _ticks = 0;
        }
    }

    public void Dismiss()
    {
        if (Current == Phase.Hidden) return;
        if (Current is Phase.Closing or Phase.FadeOut) return;
        Current = Phase.FadeOut;
        _ticks = 0;
    }

    public void Advance(int ticks = 1)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        for (int i = 0; i < ticks; i++) Tick();
    }

    private void Tick()
    {
        if (Current == Phase.Hidden) return;
        _ticks++;
        switch (Current)
        {
            case Phase.Opening:
                Expansion = Math.Min(1f, _ticks / 8f) * 0.22f;
                if (_ticks >= 8) Switch(Phase.Prompt);
                break;
            case Phase.Prompt:
                Expansion = 0.22f;
                break;
            case Phase.Expanding:
                Expansion = 0.22f + Math.Min(1f, _ticks / 8f) * 0.78f;
                if (_ticks >= 8) Switch(Phase.FadeIn);
                break;
            case Phase.FadeIn:
                Expansion = 1;
                Opacity = Math.Min(1f, _ticks / 4f);
                if (_ticks >= 4) Switch(Phase.Text);
                break;
            case Phase.Text:
                Opacity = 1;
                break;
            case Phase.FadeOut:
                Opacity = Math.Max(0, 1f - _ticks / 4f);
                if (_ticks >= 4) Switch(Phase.Closing);
                break;
            case Phase.Closing:
                Expansion = Math.Max(0, 1f - _ticks / 8f);
                if (_ticks >= 8)
                {
                    Current = Phase.Hidden;
                    Message = string.Empty;
                    Opacity = 0;
                    Expansion = 0;
                    if (_queued is { } next)
                    {
                        _queued = null;
                        ShowPrompt(next);
                    }
                }
                break;
        }
    }

    private void Switch(Phase phase) { Current = phase; _ticks = 0; }
}
