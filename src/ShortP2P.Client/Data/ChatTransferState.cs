namespace ShortP2P.Client.Data;

public enum ChatTransferState
{
    None = 0,
    Offered = 1,
    AwaitingClick = 2,
    Transferring = 3,
    Received = 4,
    Failed = 5
}