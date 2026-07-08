// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using HidApi;
using Xap;
using Xap.Hid;

Hid.Init();
try
{
    var devices = Hid.Enumerate().Where(XapHidDevice.Match).ToList();

    if (devices.Count == 0)
    {
        Console.WriteLine("No XAP devices found.");
        return;
    }

    Console.WriteLine($"Found {devices.Count} XAP device(s):");
    Console.WriteLine();

    foreach (DeviceInfo? info in devices)
        await PrintDeviceAsync(info);
}
finally
{
    Hid.Exit();
}

static async Task PrintDeviceAsync(DeviceInfo info)
{
    XapHidDevice? device = null;
    XapClient? client = null;
    try
    {
        device = XapHidDevice.TryCreate(info);
        if (device is null)
            return;
        client = await XapClient.CreateAsync(device);

        var xapVersion = await client.Xap.GetVersionAsync();
        var qmkVersion = client.Qmk.HasVersionQuery
            ? (await client.Qmk.GetVersionAsync()).ToString()
            : "n/a";

        Console.WriteLine(device.ToString());
        Console.WriteLine($"  XAP version: {xapVersion}");
        Console.WriteLine($"  QMK version: {qmkVersion}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{info.ManufacturerString} {info.ProductString} ({info.VendorId:X4}:{info.ProductId:X4}) -- error: {ex.Message}");
        Console.WriteLine();
    }
    finally
    {
        if (client is not null)
            await client.DisposeAsync(); // disposes `device` too (XapHidDevice : IDisposable)
        else
            device?.Dispose();
    }
}
