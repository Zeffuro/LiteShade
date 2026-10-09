using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LiteShade.Configuration;

namespace LiteShade.Profiles;

public sealed class ProfileRule
{
    [JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";

    public bool Enabled { get; set; } = true;

    public Guid ProfileId { get; set; }

    public List<uint> TerritoryIds { get; set; } = [];

    public uint? AreaId { get; set; }

    public List<byte> WeatherIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public uint? TerritoryId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
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

    public ProfileRule Copy()
    {
        var copy = (ProfileRule)MemberwiseClone();
        copy.TerritoryIds = TerritoryIds?.ToList()!;
        copy.WeatherIds = WeatherIds?.ToList()!;
        return copy;
    }

    public bool IsValid => Id != Guid.Empty
                           && ProfileId != Guid.Empty
                           && TerritoryIds is not null && !TerritoryIds.Contains(0)
                           && WeatherIds is not null && !WeatherIds.Contains(0)
                           && Enum.IsDefined(Activity)
                           && StartTime.HasValue == EndTime.HasValue
                           && (StartTime is null || StartTime is >= 0 and < 1440)
                           && (EndTime is null || EndTime is >= 0 and < 1440);

    public void Normalize()
    {
        if (TerritoryIds is null || TerritoryIds.Contains(0)
            || WeatherIds is null || WeatherIds.Contains(0)
            || TerritoryId == 0 || AreaId == 0 || WeatherId == 0)
        {
            Enabled = false;
        }

        AreaId = AreaId == 0 ? null : AreaId;
        TerritoryIds ??= [];
        WeatherIds ??= [];
        MigrateLegacyConditions();
        TerritoryIds = TerritoryIds.Where(territory => territory != 0).Distinct().ToList();
        WeatherIds = WeatherIds.Where(weather => weather != 0).Distinct().ToList();

        Name = ColorProfile.NormalizeName(Name, "Unnamed rule");

        if (!IsValid)
        {
            Enabled = false;
        }
    }

    private void MigrateLegacyConditions()
    {
        if (TerritoryId is { } territory) TerritoryIds.Add(territory);
        if (WeatherId is { } weather) WeatherIds.Add(weather);
        TerritoryId = null;
        WeatherId = null;
    }
}
