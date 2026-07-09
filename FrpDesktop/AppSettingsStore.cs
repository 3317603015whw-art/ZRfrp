using System.Text.Json;
using System.IO;

namespace FrpDesktop;

public sealed class AppSettingsStore
{
    private const string DefaultFrpcPath = @"E:\1\Downloads\frp_0.69.1_windows_amd64\frpc.exe";
    private const string DefaultFrpcTomlPath = @"E:\1\Downloads\frp_0.69.1_windows_amd64\frpc.toml";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettingsStore()
    {
        MigrateLegacySettings();
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(GeneratedConfigDirectory);
    }

    public string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZRfrp");

    private string LegacyAppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrpDesktop");

    public string GeneratedConfigDirectory =>
        Path.Combine(AppDataDirectory, "generated");

    public string SettingsPath =>
        Path.Combine(AppDataDirectory, "profiles.json");

    private string LegacySettingsPath =>
        Path.Combine(LegacyAppDataDirectory, "profiles.json");

    public AppState Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return CreateInitialState();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var state = JsonSerializer.Deserialize<AppState>(json, _jsonOptions);
            return EnsureValidState(state);
        }
        catch
        {
            var backupPath = Path.Combine(AppDataDirectory, $"profiles.broken.{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Copy(SettingsPath, backupPath, overwrite: true);
            return CreateInitialState();
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private void MigrateLegacySettings()
    {
        if (File.Exists(SettingsPath) || !File.Exists(LegacySettingsPath))
        {
            return;
        }

        Directory.CreateDirectory(AppDataDirectory);
        File.Copy(LegacySettingsPath, SettingsPath, overwrite: false);
    }

    public string GetGeneratedConfigPath(FrpProfile profile)
    {
        Directory.CreateDirectory(GeneratedConfigDirectory);
        var safeName = MakeSafeFileName(profile.Name);
        return Path.Combine(GeneratedConfigDirectory, $"{safeName}-{profile.Id[..Math.Min(8, profile.Id.Length)]}.toml");
    }

    private AppState CreateInitialState()
    {
        FrpProfile profile;

        if (File.Exists(DefaultFrpcTomlPath))
        {
            var toml = File.ReadAllText(DefaultFrpcTomlPath);
            profile = FrpConfigSerializer.FromToml(toml, DefaultFrpcPath, "阿里云 FRP");
        }
        else
        {
            profile = new FrpProfile
            {
                Name = "阿里云 FRP",
                FrpcPath = DefaultFrpcPath,
                ServerAddr = "120.55.2.239",
                ServerPort = 7000,
                Token = "123456"
            };
            profile.Proxies.Add(FrpConfigSerializer.CreateDefaultProxy());
        }

        return new AppState
        {
            LastProfileId = profile.Id,
            ClientFrpcPath = profile.FrpcPath,
            Profiles = new() { profile }
        };
    }

    private static AppState EnsureValidState(AppState? state)
    {
        if (state is null || state.Profiles.Count == 0)
        {
            return new AppSettingsStore().CreateInitialState();
        }

        foreach (var profile in state.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            if (profile.Proxies is null)
            {
                profile.Proxies = new();
            }
        }

        state.LastProfileId ??= state.Profiles[0].Id;
        if (string.IsNullOrWhiteSpace(state.ClientFrpcPath))
        {
            state.ClientFrpcPath = state.Profiles
                .Select(profile => profile.FrpcPath)
                .FirstOrDefault(File.Exists) ?? "";
        }

        state.NetworkProxyMode = string.IsNullOrWhiteSpace(state.NetworkProxyMode) ? "none" : state.NetworkProxyMode;
        state.NetworkProxyType = string.IsNullOrWhiteSpace(state.NetworkProxyType) ? "HTTP" : state.NetworkProxyType;
        state.NetworkProxyHost ??= "";
        state.NetworkProxyUsername ??= "";
        state.NetworkProxyPassword ??= "";

        return state;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeChars = value.Select(character => invalidChars.Contains(character) ? '_' : character);
        var safeName = new string(safeChars.ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "profile" : safeName;
    }
}
