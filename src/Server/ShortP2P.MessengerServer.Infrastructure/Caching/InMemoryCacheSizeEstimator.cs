using System.Text;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.Caching;

internal static class InMemoryCacheSizeEstimator
{
    private const int EntryOverheadBytes = 96;

    public static long Estimate(Message message)
    {
        return EntryOverheadBytes
               + Utf8Length(message.MessageId)
               + Utf8Length(message.SrcNetworkId)
               + Utf8Length(message.TgtNetworkId)
               + Utf8Length(message.EncryptedDataBase64)
               + sizeof(long) * 2;
    }

    public static long Estimate(CachedDeliveryTicket entry)
    {
        return EntryOverheadBytes
               + Utf8Length(entry.Ticket.MessageId)
               + Utf8Length(entry.SrcNetworkId)
               + sizeof(long);
    }

    private static int Utf8Length(string? value)
        => string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
}
