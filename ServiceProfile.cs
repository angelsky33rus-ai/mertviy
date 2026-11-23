using System.Text.Json;

namespace Mertviy;

internal sealed record ServiceTarget(string Name, string[] Hosts, string[] ProbeUrls);

internal sealed record Strategy(string Name, string Description, string Desync, string SplitPosition, string Repeats);

internal sealed class ProfileCatalog
{
    public IReadOnlyList<ServiceTarget> Services { get; }
    public IReadOnlyList<Strategy> Strategies { get; }

    private ProfileCatalog(ServiceTarget[] services, Strategy[] strategies)
    {
        Services = Array.AsReadOnly(services);
        Strategies = Array.AsReadOnly(strategies);
    }

    public static ProfileCatalog Load(System.Reflection.Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Не найден встроенный каталог профилей: {resourceName}");
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Каталог профилей пуст.");

        var services = catalog.Services ?? [];
        var strategies = catalog.Strategies ?? [];
        if (services.Length == 0 || strategies.Length == 0)
            throw new InvalidDataException("Каталог должен содержать сервисы и стратегии.");

        EnsureUnique(services.Select(x => x.Name), "сервисов");
        EnsureUnique(strategies.Select(x => x.Name), "стратегий");

        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Name) || service.Hosts is not { Length: > 0 } || service.ProbeUrls is not { Length: > 0 })
                throw new InvalidDataException("У каждого сервиса должны быть имя, домены и URL для проверки.");
            if (service.Hosts.Any(host => string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown))
                throw new InvalidDataException($"У сервиса {service.Name} есть некорректный домен.");
            if (service.ProbeUrls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"У сервиса {service.Name} есть некорректный HTTPS URL.");
        }

        var supportedDesync = new HashSet<string>(["fake", "multisplit", "multidisorder"], StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            var values = strategy.Desync?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (string.IsNullOrWhiteSpace(strategy.Name) || values.Length == 0 || values.Any(value => !supportedDesync.Contains(value)))
                throw new InvalidDataException("Каталог содержит неподдерживаемую стратегию.");
            if (!int.TryParse(strategy.Repeats, out var repeats) || repeats is < 1 or > 20)
                throw new InvalidDataException($"У стратегии {strategy.Name} некорректное число повторов.");
            if (strategy.SplitPosition is null || strategy.SplitPosition.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is ',' or '-' or '_')))
                throw new InvalidDataException($"У стратегии {strategy.Name} некорректная позиция разделения.");
        }

        return new ProfileCatalog(services, strategies);
    }

    private static void EnsureUnique(IEnumerable<string?> values, string kind)
    {
        var names = values.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException($"В каталоге есть пустые или повторяющиеся имена {kind}.");
    }

    private sealed class CatalogDocument
    {
        public CatalogDocument() { }

        public ServiceTarget[]? Services { get; set; }
        public Strategy[]? Strategies { get; set; }
    }
}
