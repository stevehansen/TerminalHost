using System.Text;
using Shouldly;
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;

namespace TerminalHost.Avalonia.Tests.VtNetCore;

/// <summary>
/// Guards the SO/SI/GR-invocation vs UTF-8-transport separation in VirtualTerminalController.
///
/// CursorState.Utf8 controls byte transport (multibyte UTF-8 vs raw 8-bit), NOT charset
/// invocation. SO/SI (0x0E/0x0F) and GR charset invocation only select which G-set maps to
/// display; they must not flip Utf8 off. The original vendored vtnetcore conflated the two, so a
/// stray 0x0E in a UTF-8 stream permanently switched the reader to raw bytes — after which every
/// UTF-8 lead byte (e.g. 0xE2) decoded through the GR path as 0xE2-0x80='b' (the "b" flood).
/// Only DOCS (SetLatin1 / SetUTF8) may change transport.
/// </summary>
public class Utf8CharsetDecodingTests
{
    private static (VirtualTerminalController, DataConsumer) NewTerminal()
    {
        var controller = new VirtualTerminalController();
        controller.ResizeView(80, 24);
        return (controller, new DataConsumer(controller));
    }

    [Fact]
    public void StrayShiftOut_DoesNotDisableUtf8Decoding()
    {
        var (controller, consumer) = NewTerminal();

        // SO (0x0E) followed by a UTF-8 box-drawing run: ─└┐ (each 3 bytes, lead 0xE2).
        consumer.Push(new byte[] { 0x0E });
        consumer.Push(Encoding.UTF8.GetBytes("─└┐"));

        controller.IsUtf8().ShouldBeTrue("SO/SI must not change byte transport");

        var screen = controller.GetScreenText();
        screen.ShouldContain("─└┐");
        // The original bug decoded 0xE2 lead bytes as 'b' — assert that signature is absent.
        screen.ShouldNotContain("b");
    }

    [Fact]
    public void StrayShiftIn_AfterShiftOut_KeepsUtf8()
    {
        var (controller, consumer) = NewTerminal();

        // SO then SI (0x0E, 0x0F) — both charset shifts, neither touches transport.
        consumer.Push(new byte[] { 0x0E, 0x0F });
        consumer.Push(Encoding.UTF8.GetBytes("✳ ✓ →")); // 3-byte chars (U+2733/U+2713/U+2192)

        controller.IsUtf8().ShouldBeTrue();
        var screen = controller.GetScreenText();
        screen.ShouldContain("✳");
        screen.ShouldContain("→");
    }

    [Fact]
    public void SetLatin1_StillTogglesTransport_FixLeftThisPathIntact()
    {
        var (controller, _) = NewTerminal();

        // DOCS Latin-1 (the legitimate way to select 8-bit transport) is untouched by the fix.
        controller.SetLatin1();
        controller.IsUtf8().ShouldBeFalse();

        controller.SetUTF8();
        controller.IsUtf8().ShouldBeTrue();
    }
}
