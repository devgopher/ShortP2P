using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Blobs;

namespace ShortP2P.MessengerServer.Tests.Blobs;

public class BlobUseCaseTests
{
    [Fact]
    public async Task PutGetDelete_RoundTripForParticipants()
    {
        var h = new TestHarness();
        var ciphertext = new byte[] { 1, 2, 3, 4 };

        await h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", ciphertext));

        var blob = await h.GetBlob().ExecuteAsync(new GetBlobQuery("blob-1", "bob"));
        Assert.Equal(ciphertext, blob.Ciphertext);
        Assert.Equal(4, blob.SizeBytes);

        await h.DeleteBlob().ExecuteAsync(new DeleteBlobCommand("blob-1", "alice"));
        Assert.Null(await h.Stores.BlobsRepo.FindByIdAsync("blob-1"));
    }

    [Fact]
    public async Task Put_WhenSameParticipants_IsIdempotent()
    {
        var h = new TestHarness();
        var bytes = new byte[] { 9 };
        await h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", bytes));
        await h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", bytes));
        Assert.Single(h.Stores.Blobs);
    }

    [Fact]
    public async Task Put_WhenSameIdDifferentParticipants_ThrowsConflict()
    {
        var h = new TestHarness();
        await h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", [1]));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "carol", "dave", [2])));
        Assert.Equal("Conflict", ex.Code);
    }

    [Fact]
    public async Task Get_WhenStranger_ThrowsUnauthorized()
    {
        var h = new TestHarness();
        await h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", [1]));

        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.GetBlob().ExecuteAsync(new GetBlobQuery("blob-1", "eve")));
        Assert.Equal("Unauthorized", ex.Code);
    }

    [Fact]
    public async Task Get_WhenMissing_ThrowsNotFound()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.GetBlob().ExecuteAsync(new GetBlobQuery("missing", "alice")));
        Assert.Equal("NotFound", ex.Code);
    }

    [Fact]
    public async Task Delete_WhenMissing_IsNoOp()
    {
        var h = new TestHarness();
        await h.DeleteBlob().ExecuteAsync(new DeleteBlobCommand("missing", "alice"));
    }

    [Fact]
    public async Task Put_WhenCiphertextTooLarge_ThrowsValidation()
    {
        var h = new TestHarness();
        var huge = new byte[PutBlobUseCase.MaxCiphertextBytes + 1];
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", huge)));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task Put_WhenEmptyCiphertext_ThrowsValidation()
    {
        var h = new TestHarness();
        var ex = await Assert.ThrowsAsync<UseCaseException>(() =>
            h.PutBlob().ExecuteAsync(new PutBlobCommand("blob-1", "alice", "bob", [])));
        Assert.Equal("Validation", ex.Code);
    }
}
