using System;
using LiteShade.Configuration;

namespace LiteShade.Profiles;

public sealed class ProfileRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";

    public bool Enabled { get; set; } = true;

    public Guid ProfileId { get; set; }

    public uint? TerritoryId { get; set; }

    public uint? AreaId { get; set; }

    public byte? WeatherId { get; set; }

    public int? StartTime { get; set; }
    public int? EndTime { get; set; }

    public RuleActivity Activity { get; set; }

    public bool? InCombat { get; set; }

    public bool? InGPose { get; set; }
    public bool? InCutscene { get; set; }
    public bool? InIdleCamera { get; set; }
    public bool? IsCrafting { get; set; }
    public bool? IsGathering { get; set; }
    public bool? IsMounted { get; set; }
    public bool? IsPerforming { get; set; }

    public ProfileRule Copy() => (ProfileRule)MemberwiseClone();

    public bool IsValid => Id != Guid.Empty
                           && ProfileId != Guid.Empty
                           && Enum.IsDefined(Activity)
                           && StartTime.HasValue == EndTime.HasValue
                           && (StartTime is null || StartTime is >= 0 and < 1440)
                           && (EndTime is null || EndTime is >= 0 and < 1440);

    public void Normalize()
    {
        if (TerritoryId == 0 || AreaId == 0 || WeatherId == 0)
        {
            TerritoryId = TerritoryId == 0 ? null : TerritoryId;
            AreaId = AreaId == 0 ? null : AreaId;
            WeatherId = WeatherId == 0 ? null : WeatherId;
            Enabled = false;
        }

        Name = ColorProfile.NormalizeName(Name, "Unnamed rule");

        if (!IsValid)
        {
            Enabled = false;
        }
    }
}
