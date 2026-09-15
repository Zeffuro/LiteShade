using System;

namespace LiteShade.Configuration;

[Flags]
public enum PauseOptions
{
    None = 0,
    GPose = 1,
    Cutscenes = 2,
    IdleCamera = 4,
    Portraits = 8,
}
