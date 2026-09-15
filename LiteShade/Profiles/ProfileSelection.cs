using System;

namespace LiteShade.Profiles;

public readonly record struct ProfileSelection(Guid ProfileId, Guid? RuleId);
