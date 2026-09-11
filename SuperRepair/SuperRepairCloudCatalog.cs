namespace WpfApp1.SuperRepair;

public sealed record SuperRepairCloudPackage(
    string Brand,
    string Series,
    string Device,
    string Version,
    IReadOnlyList<string> DownloadUrls,
    IReadOnlyDictionary<string, string> RequestHeaders);

public sealed class SuperRepairCloudCatalog
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _loadSeries;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<string>>> _loadDevices;
    private readonly Func<string, string, string, CancellationToken, Task<SuperRepairCloudPackage>> _resolveLatestPackage;

    public SuperRepairCloudCatalog(
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadSeries,
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>>> loadDevices,
        Func<string, string, string, CancellationToken, Task<SuperRepairCloudPackage>> resolveLatestPackage)
    {
        _loadSeries = loadSeries ?? throw new ArgumentNullException(nameof(loadSeries));
        _loadDevices = loadDevices ?? throw new ArgumentNullException(nameof(loadDevices));
        _resolveLatestPackage = resolveLatestPackage ?? throw new ArgumentNullException(nameof(resolveLatestPackage));
    }

    public Task<IReadOnlyList<string>> LoadSeriesAsync(
        string brand,
        CancellationToken cancellationToken) =>
        _loadSeries(brand, cancellationToken);

    public Task<IReadOnlyList<string>> LoadDevicesAsync(
        string brand,
        string series,
        CancellationToken cancellationToken) =>
        _loadDevices(brand, series, cancellationToken);

    public Task<SuperRepairCloudPackage> ResolveLatestPackageAsync(
        string brand,
        string series,
        string device,
        CancellationToken cancellationToken) =>
        _resolveLatestPackage(brand, series, device, cancellationToken);
}
