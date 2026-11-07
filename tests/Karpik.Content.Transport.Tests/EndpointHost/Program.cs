using Karpik.Content.Transport;

ContentReloadAddress address = ContentReloadAddress.Create(args[0], "runtime-1", "sample", ContentReloadTarget.Client);
await using var endpoint = new ContentReloadEndpoint(address, async (request, token) =>
{
    if (args.Length > 1 && args[1] == "block")
    {
        Console.WriteLine("accepted");
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
    return new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied);
});
Console.WriteLine("ready");
await Console.In.ReadLineAsync();
