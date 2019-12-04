# Singyeong.Net [![MyGet][myget-image]][myget-link] [![Pipelines][ci-pipeline-image]][ci-pipeline-link] #

An asynchronous client library for [신경][singyeong], a dynamic metadata-oriented
service mesh.

## WARNING ##

If 신경 is alpha quality software, then Singyeong.Net is pre-alpha quality
software. Expect things to break spectacularly.

## Usage ##

At the simplest level:

```cs
var client = new SingyeongClientBuilder("application id here")
    // Repeat this for how much initial metadata you have
    .AddMetadata<TValue>("name", value)
    // Repeat this for however many endpoints you have
    .AddEndpoint("ws://localhost:4000/gateway/websocket", null)
    .Build();
```
Then, wherever else:
```cs
// Cancel the cancel token to cause the client to stop... hopefully gracefully.
await client.RunAsync(cancellationToken);
```
To publish events, use:
```cs
await client.SendToAsync("application id here", payload,
    allowRestricted: true, // or false
    query: (x) =>
        x.Metadata<double>("latency") < 50.0 &&
        x.Metadata<string[]>("tags").Contains("production")
    );
```
To update/remove metadata, use:
```cs
await client.SetMetadataAsync<TValue>("key", value);
await client.RemoveMetadataAsync("key");
```
To read dispatches, use:
```cs
var reader = client.DispatchReader;
while (await reader.WaitToReadAsync(cancellationToken))
{
    while (reader.TryRead(out var dispatch))
    {
        // dispatch is the raw payload sent in the dispatch, including the
        // nonce field. This is so you can implement your own reading logic on
        // top of this, as well as get access to the nonce for indempotency if
        // necessary.
    }
}
```

## To-Do ##

- Add further error handling
- Set up CI
- Publish NuGet packages
- Add unit tests
- Clean up the codebase
- Add better failover strategies

[ci-pipeline-link]: https://gitlab.com/FiniteReality/Singyeong.Net/pipelines
[ci-pipeline-image]: https://gitlab.com/FiniteReality/Singyeong.Net/badges/master/pipeline.svg
[myget-link]: https://www.myget.org/feed/finitereality/package/nuget/Singyeong.Net
[myget-image]: https://img.shields.io/myget/finitereality/vpre/Singyeong.Net.svg?label=myget
[singyeong]: https://github.com/queer/singyeong
