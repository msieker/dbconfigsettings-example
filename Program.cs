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
        var sitePath = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "appsettings.site.json");
        Console.WriteLine(sitePath);
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development";
        cb.AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", true)
            .AddJsonFile(sitePath, true, true);
        
        var conf = cb.Build();
        
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
        Console.WriteLine(conf.GetDebugView());

        var connStr = conf["Database:SettingsConnectionString"];
        var configSource = new DbConfigurationSource(opt => opt.UseSqlite(connStr));
        cb.Add(configSource);

        conf = cb.Build();
        

        var sc = new ServiceCollection();

        sc.AddSingleton(configSource);
        sc.Configure<EmailSettings>(conf.GetSection("Email"));

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