# Singyeong.Net #

An asynchronous client library for [신경][singyeong], a dynamic metadata-oriented
service mesh.

## WARNING ##

If 신경 is alpha quality software, then Singyeong.Net is pre-alpha quality
software. Expect things to break spectacularly.

## Usage ##

At the simplest level:

```cs
var client = new SingyeongClientBuilder("application id here")
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

## To-Do ##

- Allow reading of dispatches from user code
- Allow specifying metadata so the above filters actually work
- Add further error handling
- Set up CI
- Publish NuGet packages
- Add unit tests
- Clean up the codebase
- Add better failover strategies

[singyeong]: https://github.com/queer/singyeong
