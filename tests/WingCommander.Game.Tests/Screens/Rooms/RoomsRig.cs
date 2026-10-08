using WingCommander.Core.Platform;
using WingCommander.Game.Campaign;

namespace WingCommander.Game.Tests.Screens.Rooms;

/// <summary>Helpers for the room screen tests: campaign set-up, typing, optional PNG snapshots.</summary>
internal static class RoomsRig
{
    /// <summary>Environment variable naming a directory for PNG snapshots (unset: no files are written).</summary>
    public const string SnapshotDirectoryVariable = "WC1_ROOMS_PNG_DIR";

    /// <summary>A headless game with an empty SAVEGAME.WLD and a running Vega campaign (series 1, mission 0).</summary>
    public static RoomScreenRig CreateCampaign(bool campaignActive = true)
    {
        var rig = new RoomScreenRig();
        var session = rig.Game.Session;
        session.Reset();
        session.LoadCampaignData(rig.Game.Directory, 0);
        session.CampaignActive = campaignActive;
        session.CampaignStartupMode = false;
        session.PendingCampaignIndex = -1;
        return rig;
    }

    /// <summary>Writes the displayed frame when <see cref="SnapshotDirectoryVariable"/> is set.</summary>
    public static void Snap(RoomScreenRig rig, string name)
    {
        string? directory = Environment.GetEnvironmentVariable(SnapshotDirectoryVariable);
        if (string.IsNullOrEmpty(directory))
            return;
        Directory.CreateDirectory(directory);
        rig.SaveFront(Path.Combine(directory, name + ".png"));
    }

    /// <summary>DOS scan code of a letter, digit or space (set 1).</summary>
    public static int ScanCode(char c) => char.ToUpperInvariant(c) switch
    {
        'A' => 0x1e, 'B' => 0x30, 'C' => 0x2e, 'D' => 0x20, 'E' => 0x12, 'F' => 0x21, 'G' => 0x22,
        'H' => 0x23, 'I' => 0x17, 'J' => 0x24, 'K' => 0x25, 'L' => 0x26, 'M' => 0x32, 'N' => 0x31,
        'O' => 0x18, 'P' => 0x19, 'Q' => 0x10, 'R' => 0x13, 'S' => 0x1f, 'T' => 0x14, 'U' => 0x16,
        'V' => 0x2f, 'W' => 0x11, 'X' => 0x2d, 'Y' => 0x15, 'Z' => 0x2c,
        '1' => 0x02, '2' => 0x03, '3' => 0x04, '4' => 0x05, '5' => 0x06, '6' => 0x07, '7' => 0x08,
        '8' => 0x09, '9' => 0x0a, '0' => 0x0b, ' ' => 0x39,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "No scan code."),
    };

    /// <summary>Windows virtual-key code of a letter, digit or space.</summary>
    public static int VirtualKey(char c) => c == ' ' ? 0x20 : char.ToUpperInvariant(c);

    /// <summary>Presses one character key (scan code and virtual key) at <paramref name="at"/>.</summary>
    public static void Key(RoomScreenRig rig, double at, char c) => rig.Key(at, ScanCode(c), VirtualKey(c));

    /// <summary>Types the text one key every <paramref name="step"/> ms; returns the time after the last key.</summary>
    public static double Type(RoomScreenRig rig, double at, string text, double step = 200)
    {
        foreach (char c in text)
        {
            Key(rig, at, c);
            at += step;
        }
        return at;
    }

    public static void Enter(RoomScreenRig rig, double at) => rig.Key(at, 0x1c, 0x0d);

    public static void Escape(RoomScreenRig rig, double at) => rig.Key(at, 0x01, 0x1b);

    public static void Backspace(RoomScreenRig rig, double at) => rig.Key(at, 0x0e, 0x08);

    public static void Space(RoomScreenRig rig, double at) => rig.Key(at, 0x39, 0x20);

    /// <summary>Presses a key and releases it <paramref name="duration"/> ms later.</summary>
    public static void HoldKey(RoomScreenRig rig, double at, int scanCode, int virtualKey, double duration)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), at);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), at + duration);
    }

    /// <summary>Holds left Shift around one character key.</summary>
    public static void ShiftedKey(RoomScreenRig rig, double at, char c)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, 0x2a, 0x10, 0, 0, 0, HostModifiers.Shift, false), at);
        Key(rig, at + 30, c);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, 0x2a, 0x10, 0, 0, 0, HostModifiers.None, false), at + 120);
    }

    /// <summary>Moves the pointer without clicking.</summary>
    public static void Move(RoomScreenRig rig, double at, int x, int y) =>
        rig.Runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, x, y, 0, HostModifiers.None, false), at);

    /// <summary>The raw bytes of one SAVEGAME.WLD slot.</summary>
    public static byte[] ReadSlotBytes(string path, int slot) =>
        File.ReadAllBytes(path).AsSpan(slot * SaveGameSlot.Size, SaveGameSlot.Size).ToArray();

    /// <summary>Counts pixels of <paramref name="colour"/> in a rectangle of the displayed frame.</summary>
    public static int CountColour(RoomScreenRig rig, byte colour, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            var row = rig.Front.Row(y);
            for (int x = left; x <= right; x++)
            {
                if (row[x] == colour)
                    count++;
            }
        }
        return count;
    }
}
