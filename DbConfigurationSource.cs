using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace ConfigurationStorage;

/*
 * IConfigurationSource for the database. I'm not a huge fan
 * of what I did to expose the actual provider, but I did it this way
 * for simplicity.
 */
public class DbConfigurationSource : IConfigurationSource
{
    private readonly Action<DbContextOptionsBuilder> _optionsAction;

    private DbConfigurationProvider _provider;

    private DbConfigurationProvider GetProvider()
    {
        return _provider ??= new DbConfigurationProvider(_optionsAction);
    }

    public DbConfigurationSource(Action<DbContextOptionsBuilder> optionsAction) => _optionsAction = optionsAction;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => GetProvider();

    public DbConfigurationProvider Provider => GetProvider();
}

/*
 * The actual provider that has the db access logic. For this demo, it has its own
 * dbcontext it has full control over, but this could also be the main app one, too.
 */
public class DbConfigurationProvider : ConfigurationProvider
{
    private readonly Action<DbContextOptionsBuilder> _optionsAction;
    private DbContextOptions<ConfigurationDbContext> _options = null!;
    private IList<ConfigSetting> _settings = new List<ConfigSetting>();

    public DbConfigurationProvider(Action<DbContextOptionsBuilder> optionsAction)
    {
        _optionsAction = optionsAction;
    }

    /*
     * Just finishing setup of the dbcontextoptions, and making sure the db
     * exists. Didn't want to bother with migrations for this.
     */
    private void InitDb()
    {
        var builder = new DbContextOptionsBuilder<ConfigurationDbContext>();
        _optionsAction(builder);
        _options = builder.Options;

        using var dbContext = new ConfigurationDbContext(_options);
        dbContext.Database.EnsureCreated();
    }

    /*
     * Reads the settings from the database. The Data member it puts them into
     * is part of the ConfigurationProvider base class. Internally, all the configuration
     * stuff works by merging the Data dictionaries of the various configuration providers
     * together, with ones specified later in the builder taking priority for the values.
     *
     * The reload parameter mainly controls if the OnReload method is called after we load here,
     * to notify the configuration bits that things have changed.
     */
    private void Load(bool reload)
    {
        if (!reload)
        {
            InitDb();
        }
        using var dbContext = new ConfigurationDbContext(_options);
        _settings = dbContext.ConfigSettings.ToList();

        Data = new Dictionary<string, string>();

        foreach (var s in _settings)
        {
            Data[s.Section + ":" + s.Name] = s.Value;
        }

        if (reload)
        {
            OnReload();
        }
    }

    /*
     * Basic Load method specified on the base class, actual implementation in our custom load method.
     */
    public override void Load()
    {
        Load(false);
    }

    /*
     * This bit of code takes a class storing configuration settings, and turns it into a dictionary,
     * eliding any properties that have the default value for their type. Any public properties that aren't
     * a value type or a string get this called on them recursively, and then get their parent member name prefixed
     * on their name
     */

    private static object? GetDefaultValue(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
    private static Dictionary<string, string> ObjectToDictionary(object obj)
    {
        var propDict = new Dictionary<string, string>();
        foreach (var p in obj.GetType().GetProperties())
        {
            if (p.PropertyType.IsValueType || p.PropertyType == typeof(string))
            {
                var value = p.GetValue(obj);
                if (value != null && value != GetDefaultValue(p.PropertyType))
                    propDict[p.Name] = value.ToString()!;
            }
            else
            {
                var value = p.GetValue(obj);
                if (value == null) continue;
                var children = ObjectToDictionary(value);
                foreach (var kv in children)
                {
                    propDict[p.Name + ":" + kv.Key] = kv.Value;
                }
            }
        }
        return propDict;
    }

    /*
     * Updates settings for a particular section. Most of this is dealing with properties that
     * have changed to their default value (eg a string having a value to being null/empty) and cleaning
     * those out.
     *
     * Once the settings are updated, write them to the database and reload the setting values.
     */
    public void UpdateSection<TSettings>(string sectionName, TSettings options)
    {
        var propDict = ObjectToDictionary(options ?? throw new ArgumentNullException(nameof(options)));
        using var dbContext = new ConfigurationDbContext(_options);

        var sectionValues = dbContext.ConfigSettings.Where(s => s.Section == sectionName).ToList();

        var valuesToDelete = new List<ConfigSetting>(sectionValues);

        foreach (var kv in propDict)
        {
            var existing = sectionValues.FirstOrDefault(s => s.Name == kv.Key);
            if (existing != null)
            {
                existing.Value = kv.Value;
                valuesToDelete.Remove(existing);
            }
            else
            {
                dbContext.ConfigSettings.Add(new ConfigSetting { Section = sectionName, Name = kv.Key, Value = kv.Value });
            }
        }

        if (valuesToDelete.Any())
        {
            dbContext.ConfigSettings.RemoveRange(valuesToDelete);
        }

        dbContext.SaveChanges();
        Load(true);
    }

    public void UpdateSection<TSettings>(TSettings options) => UpdateSection(typeof(TSettings).Name, options);
}
