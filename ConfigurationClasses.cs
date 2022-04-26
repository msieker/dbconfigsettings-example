using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConfigurationStorage;

public class EmailAuthenticationSettings
{
    public override string ToString()
    {
        return $"{nameof(UserName)}: {UserName}, {nameof(Password)}: {Password}, {nameof(SomeUnusedValued)}: {SomeUnusedValued}";
    }

    public string UserName { get; set; }
    public string Password { get; set; }
    public string SomeUnusedValued { get; set; }
}

public class EmailSettings
{
    public string Host { get; set; }
    public int Port { get; set; }
    public EmailAuthenticationSettings Authentication { get; set; } = new();

    public override string ToString()
    {
        return $"{nameof(Host)}: {Host}, {nameof(Port)}: {Port}, {nameof(Authentication)}: {Authentication}";
    }
}

public class DatabaseSettings
{
    public string SettingsConnectionString { get; set; }
}

public class SiteSettings
{
    public DatabaseSettings Database { get; set; } = new();
}