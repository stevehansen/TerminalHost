// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using System.Runtime.InteropServices;

namespace TerminalHost.CmdPal;

/// <summary>
/// The extension entry point instantiated by Command Palette via COM.
/// Returns the <see cref="TerminalHostCommandsProvider"/> that supplies
/// top-level commands and dock bands.
/// </summary>
[ComVisible(true)]
[Guid("9EB4EA8B-F590-4710-80D4-C8CD1792CED9")]
[ComDefaultInterface(typeof(IExtension))]
public sealed partial class TerminalHostExtension : IExtension
{
    private readonly ManualResetEvent _extensionDisposedEvent = new(false);

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => new TerminalHostCommandsProvider(),
            _ => null,
        };
    }

    public void Dispose()
    {
        _extensionDisposedEvent.Set();
    }
}
