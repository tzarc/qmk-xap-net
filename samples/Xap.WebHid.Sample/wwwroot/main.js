// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

import { dotnet } from './_framework/dotnet.js'
import * as webhid from './webhid.js'

const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();

setModuleImports('main.js', {
    dom: {
        setStatus: text => document.querySelector('#status').innerText = text
    }
});
setModuleImports('webhid', webhid);

const config = getConfig();
const appExports = await getAssemblyExports(config.mainAssemblyName);
const webHidExports = await getAssemblyExports('Xap.WebHid.dll');
webhid.setExports(webHidExports);

document.getElementById('connect').addEventListener('click', e => {
    appExports.App.Connect();
    e.preventDefault();
});

// run the C# Main() method and keep the runtime process running and executing further API calls
await runMain();
