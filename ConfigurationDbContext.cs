using Microsoft.EntityFrameworkCore;

namespace ConfigurationStorage;

public class ConfigurationDbContext : DbContext
{
    public ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : base(options)
    {

    }

    public DbSet<ConfigSetting> ConfigSettings => Set<ConfigSetting>();
}

public class ConfigSetting
{
    public int Id { get; set; }
    public string Section { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
    public bool Encrypted { get; set; }
}
