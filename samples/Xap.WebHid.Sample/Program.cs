// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices.JavaScript;
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
            var xapVersion = await client.Xap.GetVersionAsync();
            var qmkVersion = client.Qmk.HasVersionQuery
                ? (await client.Qmk.GetVersionAsync()).ToString()
                : "n/a";

            SetStatus($"XAP {xapVersion}, QMK {qmkVersion}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }
}
