using TerminalHost.Domain;

namespace TerminalHost.Services;

public class ProfileRegistry
{
    private readonly ConfigurationService _configurationService;
    private AppConfiguration _configuration;

    public IReadOnlyList<Profile> Profiles => _configuration.Profiles.AsReadOnly();
    public IReadOnlyList<LinkPattern> LinkPatterns => _configuration.LinkPatterns.AsReadOnly();
    public AppSettings Settings => _configuration.Settings;

    public event EventHandler? ProfilesChanged;

    public ProfileRegistry(ConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _configuration = _configurationService.Load();
    }

    public Profile? GetProfile(string id)
    {
        return _configuration.Profiles.FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<Profile> GetAutoStartProfiles()
    {
        return _configuration.Profiles.Where(p => p.AutoStart);
    }

    public void AddProfile(Profile profile)
    {
        _configuration.Profiles.Add(profile);
        Save();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateProfile(Profile profile)
    {
        var existing = GetProfile(profile.Id);
        if (existing != null)
        {
            var index = _configuration.Profiles.IndexOf(existing);
            _configuration.Profiles[index] = profile;
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveProfile(string id)
    {
        var profile = GetProfile(id);
        if (profile != null)
        {
            _configuration.Profiles.Remove(profile);
            Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reload()
    {
        _configuration = _configurationService.Load();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        _configurationService.Save(_configuration);
    }
}
