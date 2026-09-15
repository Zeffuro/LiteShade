namespace LiteShade.Graphics;

internal static class FilterStatus
{
    public const string WaitingForScene = "Waiting for the main scene.";
    public const string Unavailable = "Native colour filter unavailable.";
    public const string PostEffectsDisabled = "Scene post-effects are inactive.";
    public const string Disabled = "Disabled or waiting for gameplay.";
    public const string Neutral = "Neutral profile.";
    public const string WaitingForGameplay = "Waiting for gameplay.";
    public const string Cutscene = "Paused during cutscenes.";
    public const string WaitingForGraphics = "Waiting for graphics settings.";
    public const string Portrait = "Paused during portrait mode.";
    public const string GPose = "Paused during GPose.";
    public const string IdleCamera = "Paused during idle camera.";
    public const string WaitingForFilter = "Waiting for the game's colour filter.";
    public const string ColourCurve = "Paused for an unsupported scene colour curve.";
    public const string ShadowEffect = "Paused for an unsupported shadow colour effect.";
    public const string SceneOutOfRange = "Scene colours are outside the supported range.";
    public const string CombinedOutOfRange = "Combined colours are outside the supported range.";
    public const string Active = "Active";
    public const string PassNotDrawn = "The scene did not draw its colour pass.";
}
