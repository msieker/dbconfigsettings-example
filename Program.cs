using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConfigurationStorage;

public class Program
{
    public static void Main(string[] args)
    {
        var cb = new ConfigurationBuilder();

        // This bit with sitePath is so the site.json file gets written into bin, instead of the current directory.
        var sitePath = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "appsettings.site.json");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development";

        /*
         * Add some preliminary JSON files for settings. Note the second true on the third one, this
         * allows the configuration to reload on change.
         */
        cb.AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", true)
            .AddJsonFile(sitePath, true, true);
        
        // Make a preliminary configuration object
        var conf = cb.Build();
        
        /*
         * Check for a database configuration setting. For the sake of this PoC, assume the
         * database settings are in the site-specific settings. If they don't exist, create
         * the settings file and reload the config.
         */
        var dbSection = conf.GetSection("Database");
        if (!dbSection.Exists())
        {
            Console.WriteLine("No existing site specific json file. Creating.");
            var siteSettings =  new SiteSettings()
            {
                Database = new DatabaseSettings
                {
                    SettingsConnectionString = "Filename=settings.db3"
                }
            };

            File.WriteAllText(sitePath, JsonSerializer.Serialize(siteSettings));
            conf.Reload();
        }

        // Dump out config to show what's going on at this point.
        Console.WriteLine(conf.GetDebugView());

        // Set up the DbConfigurationSource with the connection string from the config file.
        var connStr = conf["Database:SettingsConnectionString"];
        var configSource = new DbConfigurationSource(opt => opt.UseSqlite(connStr));
        cb.Add(configSource);

        // The real actual configuration provider
        conf = cb.Build();
        
        var sc = new ServiceCollection();

        // Put the configuration source into the DI container so we can get at it later
        sc.AddSingleton(configSource);
        // Add email settings to the DI container
        sc.Configure<EmailSettings>(conf.GetSection("Email"));

        /*
         * The rest of this is just finding and running a set of steps to give some demo behavior.
         * Before each step, the name of the step will be printed, and after it runs, the resulting configuration
         * is printed
         *
         * For the POC, the order of options is write a full known configuration to the database, read it back out,
         * update a single property on it and write it back, and then write it out again.
         */
        Type ti = typeof(IExampleAction);
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type t in asm.GetTypes())
            {
                if (ti.IsAssignableFrom(t) && !t.IsInterface)
                {
                    sc.AddTransient(t);
                }
            }
        }

        var sp = sc.BuildServiceProvider();

        var steps = new[] { typeof(CreateSettings), typeof(ReadSettings), typeof(UpdateSettings), typeof(ReadSettings) };

        foreach (var step in steps)
        {
            var instance = sp.GetRequiredService(step) as IExampleAction;
            Console.WriteLine(step.Name);
            instance.Run();
            Console.WriteLine(conf.GetDebugView());
        }
    }
}

public interface IExampleAction
{
    void Run();
}

public class CreateSettings : IExampleAction
{
    private DbConfigurationSource _dbConfig;

    public CreateSettings(DbConfigurationSource dbConfig)
    {
        _dbConfig = dbConfig;
    }

    public void Run()
    {
        var settings = new EmailSettings
        {
            Host = "example.com",
            Port = 25,
            Authentication = new EmailAuthenticationSettings
            {
                Password = "password",
                UserName = "user@example.com"
            }
        };

        _dbConfig.Provider.UpdateSection("Email", settings);
    }
}

public class UpdateSettings : IExampleAction
{
    private DbConfigurationSource _dbConfig;
    private IOptionsSnapshot<EmailSettings> _settings;
    public UpdateSettings(DbConfigurationSource dbConfig, IOptionsSnapshot<EmailSettings> settings)
    {
        _dbConfig = dbConfig;
        _settings = settings;
    }

    public void Run()
    {
        _settings.Value.Authentication.SomeUnusedValued = "Unused";
        _dbConfig.Provider.UpdateSection("Email", _settings.Value);
    }
}

public class ReadSettings : IExampleAction
{
    private IOptionsSnapshot<EmailSettings> _settings;

    public ReadSettings(IOptionsSnapshot<EmailSettings> settings)
    {
        _settings = settings;
    }

    public void Run()
    {
        Console.WriteLine(_settings.Value);
    }
}