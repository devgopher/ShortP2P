namespace ShortP2P.Client.Data;

/// <summary>Состояние доставки для исходящих сообщений (входящие — <see cref="NotApplicable" />).</summary>
public enum MessageDeliveryStatus
{
    NotApplicable = 0,
    Pending = 1,
    Delivered = 2,
    Failed = 3
}