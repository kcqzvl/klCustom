using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StartStyleToolbox;

internal sealed class Profile
{
    public Dictionary<string, StyleOptions> Regions { get; set; } = new(StringComparer.Ordinal);
    public bool MainEnabled { get; set; }

    private static string ProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartStyleToolbox", "profile.json");

    internal static Profile Load()
    {
        try
        {
            if (!File.Exists(ProfilePath))
                return new();
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(ProfilePath)) ?? new();
            profile.Regions = Region.All.Where(region => profile.Regions?.ContainsKey(region.Id) == true)
                .ToDictionary(region => region.Id, region => profile.Regions![region.Id]);
            foreach (var options in profile.Regions.Values)
                StyleBrushes.Validate(options);
            return profile;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException)
        {
            return new();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        string temporary = ProfilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ProfilePath, overwrite: true);
    }
}
