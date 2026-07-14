// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices.JavaScript;
using System.Text;
using Xap.WebHid;

Console.WriteLine("Xap.WebHid.Sample ready.");

partial class App
{
    // Same usage page/usage XapHidDevice matches on native platforms (Xap.Hid/XapHidDevice.cs).
    private const int XapUsagePage = 0xFF51;
    private const int XapUsage = 0x0058;

    [JSImport("dom.setStatus", "main.js")]
    internal static partial void SetStatus(string text);

    /// <summary>Invoked by main.js from the Connect button's click handler (a real user gesture,
    /// as WebHID's requestDevice() requires).</summary>
    [JSExport]
    internal static async Task Connect()
    {
        SetStatus("Requesting device...");
        try
        {
            var transport = await WebHidTransport.RequestAsync(XapUsagePage, XapUsage);
            if (transport is null)
            {
                SetStatus("No device selected.");
                return;
            }

            await using var client = await Xap.XapClient.CreateAsync(transport);

            // Same capability-gated diagnostic report as samples/Xap.Scanner.
            var sb = new StringBuilder();
            sb.AppendLine($"XAP version:        {await client.Xap.GetVersionAsync()}");
            if (client.Xap.HasGetSecureStatus)
                sb.AppendLine($"Secure status:      {await client.Xap.GetSecureStatusAsync()}");

            if (client.Qmk.HasVersionQuery)
                sb.AppendLine($"QMK version:        {await client.Qmk.GetVersionAsync()}");
            if (client.Qmk.HasGetBoardIdentifiers)
            {
                var id = await client.Qmk.GetBoardIdentifiersAsync();
                sb.AppendLine($"Board identifiers:  {id.VendorId:X4}:{id.ProductId:X4} rev {id.ProductVersion:X4}, QMK id 0x{id.QmkUniqueIdentifier:X8}");
            }
            if (client.Qmk.HasGetBoardManufacturer)
                sb.AppendLine($"Manufacturer:       {await client.Qmk.GetBoardManufacturerAsync()}");
            if (client.Qmk.HasGetProductName)
                sb.AppendLine($"Product:            {await client.Qmk.GetProductNameAsync()}");
            if (client.Qmk.HasGetHardwareId)
                sb.AppendLine($"Hardware ID:        {string.Concat((await client.Qmk.GetHardwareIdAsync()).Select(u => u.ToString("X8")))}");
            if (client.Qmk.HasGetConfigBlobLength)
                sb.AppendLine($"Config blob:        {await client.Qmk.GetConfigBlobLengthAsync()} bytes");

            if (client.Keymap.HasGetLayerCount)
                sb.AppendLine($"Keymap layers:      {await client.Keymap.GetLayerCountAsync()}");
            if (client.Remapping.HasGetDynamicLayerCount)
                sb.AppendLine($"Remappable layers:  {await client.Remapping.GetDynamicLayerCountAsync()}");

            if (client.Lighting.HasBacklight && client.Lighting.Backlight.HasGetEnabledEffects)
                sb.AppendLine($"Backlight effects:  0x{await client.Lighting.Backlight.GetEnabledEffectsAsync():X2}");
            if (client.Lighting.HasRgblight && client.Lighting.Rgblight.HasGetEnabledEffects)
                sb.AppendLine($"RGB light effects:  0x{await client.Lighting.Rgblight.GetEnabledEffectsAsync():X16}");
            if (client.Lighting.HasRgbMatrix && client.Lighting.RgbMatrix.HasGetEnabledEffects)
                sb.AppendLine($"RGB matrix effects: 0x{await client.Lighting.RgbMatrix.GetEnabledEffectsAsync():X16}");

            SetStatus(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }
}
