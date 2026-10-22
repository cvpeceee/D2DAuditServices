namespace D2DAudit.Application;

public sealed record NotificationDto(string Title, string Body, string Href, DateTimeOffset At);

public sealed record DeviceDto(string OwnerName, string Platform, string Serial, DateOnly LastSeenOn);

public sealed record ResourceDto(string Provider, string Kind, string Name, string Detail);

public interface IClientRead
{
    Task<IReadOnlyList<HistoryPoint>> HistoryAsync(CancellationToken cancellationToken);

    Task<int> DeviceCountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDto>> NotificationsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceDto>> DevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ResourceDto>> ResourcesAsync(CancellationToken cancellationToken);
}
