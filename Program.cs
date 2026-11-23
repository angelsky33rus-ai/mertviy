using System.Reflection;

namespace Mertviy;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var profiles = ProfileCatalog.Load(assembly, "Mertviy.Config.service-profiles.json");
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
        var updater = new GitHubReleaseUpdater(version);
        Application.Run(new MainForm(profiles, updater, version, ReadEngineVersion(assembly)));
    }

    private static string ReadEngineVersion(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream("Mertviy.Engine.ENGINE_VERSION.txt");
        if (stream is null) return "версия не указана";
        using var reader = new StreamReader(stream);
        return reader.ReadLine() ?? "версия не указана";
    }
}
