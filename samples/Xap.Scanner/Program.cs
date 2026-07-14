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

        Console.WriteLine(device.ToString());
        Console.WriteLine($"  XAP version:        {await client.Xap.GetVersionAsync()}");
        if (client.Xap.HasGetSecureStatus)
            Console.WriteLine($"  Secure status:      {await client.Xap.GetSecureStatusAsync()}");

        if (client.Qmk.HasVersionQuery)
            Console.WriteLine($"  QMK version:        {await client.Qmk.GetVersionAsync()}");
        if (client.Qmk.HasKeycodesVersionQuery)
            Console.WriteLine($"  Keycodes version:   {await client.Qmk.GetKeycodesVersionAsync()}");
        if (client.Qmk.HasGetBoardIdentifiers)
        {
            var id = await client.Qmk.GetBoardIdentifiersAsync();
            Console.WriteLine($"  Board identifiers:  {id.VendorId:X4}:{id.ProductId:X4} rev {id.ProductVersion:X4}, QMK id 0x{id.QmkUniqueIdentifier:X8}");
        }
        if (client.Qmk.HasGetBoardManufacturer)
            Console.WriteLine($"  Manufacturer:       {await client.Qmk.GetBoardManufacturerAsync()}");
        if (client.Qmk.HasGetProductName)
            Console.WriteLine($"  Product:            {await client.Qmk.GetProductNameAsync()}");
        if (client.Qmk.HasGetHardwareId)
            Console.WriteLine($"  Hardware ID:        {string.Concat((await client.Qmk.GetHardwareIdAsync()).Select(u => u.ToString("X8")))}");
        if (client.Qmk.HasGetConfigBlobLength)
            Console.WriteLine($"  Config blob:        {await client.Qmk.GetConfigBlobLengthAsync()} bytes");

        if (client.Keymap.HasGetLayerCount)
            Console.WriteLine($"  Keymap layers:      {await client.Keymap.GetLayerCountAsync()}");
        if (client.Remapping.HasGetDynamicLayerCount)
            Console.WriteLine($"  Remappable layers:  {await client.Remapping.GetDynamicLayerCountAsync()}");

        if (client.Lighting.HasBacklight && client.Lighting.Backlight.HasGetEnabledEffects)
            Console.WriteLine($"  Backlight effects:  0x{await client.Lighting.Backlight.GetEnabledEffectsAsync():X2}");
        if (client.Lighting.HasRgblight && client.Lighting.Rgblight.HasGetEnabledEffects)
            Console.WriteLine($"  RGB light effects:  0x{await client.Lighting.Rgblight.GetEnabledEffectsAsync():X16}");
        if (client.Lighting.HasRgbMatrix && client.Lighting.RgbMatrix.HasGetEnabledEffects)
            Console.WriteLine($"  RGB matrix effects: 0x{await client.Lighting.RgbMatrix.GetEnabledEffectsAsync():X16}");

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
