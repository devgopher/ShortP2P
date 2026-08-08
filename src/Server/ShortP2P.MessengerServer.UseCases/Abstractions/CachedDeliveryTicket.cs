using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Delivery ticket in cache, indexed by the message sender's network id.</summary>
public sealed record CachedDeliveryTicket(DeliveryTicket Ticket, string SrcNetworkId);
