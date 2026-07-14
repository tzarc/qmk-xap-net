# XAP C# bindings

C# client bindings for [QMK](https://qmk.fm)'s XAP (eXtensible Application Protocol) — the HID protocol QMK firmware exposes for host-side configuration and control.

A Roslyn source generator reads a XAP spec (the same JSON QMK publishes) at compile time and emits a strongly typed `XapClient` for that exact spec version: one property per subsystem, one async method per route, one event per broadcast. The spec-invariant machinery — framing, token correlation, timeouts, capability gating, lifecycle — lives in a hand-written runtime library underneath.

## Packages

| Package | What it is |
|---|---|
| `Xap.Core` | Spec-invariant runtime: `XapClientBase`, `IXapTransport`, framing, request/response correlation, exceptions. |
| `Xap.SourceGenerator` | Incremental source generator. Turns a `<XapSpec>` JSON file into the generated `XapClient`, subsystem classes, payload structs, enums, and broadcast events. |
| `Xap.Hid` | Native transport over [hidapi](https://github.com/libusb/hidapi) (via HidApi.Net). Works with Native AOT. |
| `Xap.WebHid` | Browser-wasm transport over WebHID, for .NET apps running in Chromium browsers. |

All packages target .NET 10 (the generator itself targets `netstandard2.0`, as Roslyn requires).

## Quick start

Reference the generator and a transport, then point `<XapSpec>` at a spec file:

```xml
<ItemGroup>
  <PackageReference Include="Xap.SourceGenerator" Version="1.0.0" PrivateAssets="all" />
  <PackageReference Include="Xap.Hid" Version="1.0.0" />
</ItemGroup>

<ItemGroup>
  <XapSpec Include="spec/xap_0.3.0.json" />
</ItemGroup>
```

The generator emits `Xap.XapClient` into your compilation. Talk to a keyboard:

```csharp
using HidApi;
using Xap;
using Xap.Hid;

Hid.Init();
try
{
    DeviceInfo? info = Hid.Enumerate().FirstOrDefault(XapHidDevice.Match);
    if (info is null)
        return;

    await using var client = await XapClient.CreateAsync(XapHidDevice.TryCreate(info)!);

    Console.WriteLine($"XAP {await client.Xap.GetVersionAsync()}");
    if (client.Qmk.HasVersionQuery)
        Console.WriteLine($"QMK {await client.Qmk.GetVersionAsync()}");

    client.LogMessageReceived += payload =>
    {
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(payload.Span));
        return Task.CompletedTask;
    };
}
finally
{
    Hid.Exit();
}
```

`CreateAsync` queries the device's capabilities up front. Routes the firmware doesn't enable throw `XapRouteUnavailableException` before anything is sent; `Has*` properties let you check first. Secure routes are gated on the device's reported secure status (`client.SecureUnlocked`).

## Spec files

`spec/` carries the XAP spec versions (0.0.1 through 0.3.1) as published by [QMK](https://github.com/qmk/qmk_firmware). Pick the version your firmware speaks; the generated client only contains what that spec declares.

## Samples

- **`samples/Xap.Scanner`** — Native AOT console app. Enumerates attached XAP devices over hidapi and prints each one's XAP and QMK versions.
- **`samples/Xap.WebHid.Sample`** — browser-wasm app. Connects to a device through the WebHID picker and queries it from C# running in the browser.

Both consume the NuGet packages from `nupkgs/` rather than project references, so they double as end-to-end package tests.

## Building and testing

Builds run inside the `mcr.microsoft.com/dotnet/sdk:10.0-aot` Docker image, so no local .NET workloads are needed. The one host-side .NET dependency is the [Husky.Net](https://alirezanet.github.io/Husky.Net/) pre-commit hook, which formats staged C# files (the formatting itself also runs in Docker). Enable it once after cloning:

```sh
dotnet tool restore
dotnet husky install
```

```sh
scripts/pack-packages.sh          # pack all four packages into nupkgs/
scripts/build-scanner.sh          # pack, then AOT-publish the scanner sample
scripts/build-webhid-sample.sh    # pack, then publish the wasm sample

docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp \
  -v "$PWD:/repo" -w /repo mcr.microsoft.com/dotnet/sdk:10.0-aot \
  dotnet test Xap.slnx -c Release
```

The test suite compiles the generator against the real spec files and drives the generated client through an in-memory transport, so generator changes are exercised end to end.

## License

[MIT](LICENSE). The XAP spec files under `spec/` originate from [qmk_firmware](https://github.com/qmk/qmk_firmware).
