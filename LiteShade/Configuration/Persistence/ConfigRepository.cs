using System;
using Dalamud.Plugin.Services;
using LiteShade.Helpers;

namespace LiteShade.Configuration.Persistence;

public static class ConfigRepository
{
    public static string? SaveError { get; private set; }

    public static SystemConfiguration LoadOrDefault()
    {
        var config = Plugin.PluginInterface.GetPluginConfig() as SystemConfiguration ?? new SystemConfiguration();
        if (config.Version > SystemConfiguration.CurrentVersion)
        {
            throw new NotSupportedException("This configuration needs a newer version of LiteShade.");
        }

        config.EnsureInitialized();
        return config;
    }

    public static void Save(SystemConfiguration config) => SaveImmediate(config);

    public static void SaveImmediate(SystemConfiguration? config)
    {
        if (config is null)
        {
            return;
        }

        try
        {
            config.EnsureInitialized();
            Plugin.PluginInterface.SavePluginConfig(config);
            SaveError = null;
        }
        catch (Exception exception)
        {
            SaveError = "Could not save configuration. Check the Dalamud log.";
            IPluginLog.Get().Error(exception, "Could not save LiteShade configuration.");
        }
    }
}
