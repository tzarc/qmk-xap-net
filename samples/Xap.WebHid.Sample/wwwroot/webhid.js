// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

// Thin navigator.hid wrapper registered as the "webhid" JSImport module (see main.js's
// setModuleImports('webhid', webhid)) for src/Xap.WebHid/WebHidTransport.cs to call into.
let device = null;
let webHidExports = null;

export function setExports(exports) {
    webHidExports = exports;
}

export async function requestDevice(usagePage, usage) {
    const devices = await navigator.hid.requestDevice({ filters: [{ usagePage, usage }] });
    if (devices.length === 0) return false;

    device = devices[0];
    await device.open();
    device.addEventListener("inputreport", event => {
        // event.data is a DataView that does NOT necessarily start at byte 0 of its backing
        // buffer -- slicing via .buffer alone (without byteOffset/byteLength) can read leading
        // garbage or the wrong length. This is the most common WebHID interop mistake (see MDN's
        // HIDDevice.oninputreport docs).
        const data = new Uint8Array(event.data.buffer, event.data.byteOffset, event.data.byteLength);
        webHidExports.Xap.WebHid.WebHidTransport.OnInputReport(data);
    });
    return true;
}

export async function write(data) {
    try {
        // sendReport() returns a promise; leaving it unawaited would risk a rejection (e.g. a
        // reportId mismatch) going unnoticed as an easy-to-miss "Uncaught (in promise)".
        await device.sendReport(0, data);
    } catch (err) {
        console.error("XAP sendReport failed:", err);
    }
}

export function close() {
    device?.close();
    device = null;
}
