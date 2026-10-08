using WingCommander.Core.Numerics;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>Thirty firework slots: 8 frames each in one of three variants of TITLE.VGA section 17.</summary>
/// <remarks>C: aFireworks[30] (FireworkState), InitializeFireworks (0x42D270), TheEndFireWorks (0x42D2A0), music.c.</remarks>
internal sealed class Fireworks
{
    public const int SlotCount = 30;

    private readonly Slot[] _slots = new Slot[SlotCount];

    /// <remarks>C: pFireworkShape.</remarks>
    public ShapeTable? Shape { get; set; }

    /// <remarks>C: InitializeFireworks (0x42D270).</remarks>
    public void Initialize()
    {
        for (int i = 0; i < SlotCount; i++)
            _slots[i].Frame = -1;
    }

    /// <summary>Starts a firework in the first free slot at a random position and variant.</summary>
    /// <remarks>C: the launch loop of ShowVictoryScreen (0x439910, screens.c).</remarks>
    public void Launch(CRandom random, int right, int bottom)
    {
        for (int index = 0; index < SlotCount; index++)
        {
            if (_slots[index].Frame != -1)
                continue;
            _slots[index].Frame = 0;
            _slots[index].X = random.InRange(0, right);
            _slots[index].Y = random.InRange(0, bottom);
            _slots[index].Variant = random.InRange(0, 2);
            break;
        }
    }

    /// <summary>
    /// Draws and advances the first <paramref name="count"/> slots (last to first) and returns the
    /// number of free slots. A finished firework stops the sound effects (FlushSoundEffectsAndLog);
    /// the launch sound call (SoundFxTick) does nothing in this build.
    /// </summary>
    /// <remarks>C: TheEndFireWorks (0x42D2A0, music.c).</remarks>
    public short Draw(Wc1Game game, Viewport viewport, int count)
    {
        short emptyCount = 0;
        int index = count;
        while (--index >= 0)
        {
            ref var slot = ref _slots[index];
            if (slot.Frame == -1)
            {
                emptyCount++;
                continue;
            }
            game.Graphics.DrawSpriteDefault(viewport, slot.X, slot.Y, Shape, slot.Frame + slot.Variant * 8);
            if (slot.Frame++ == 7)
            {
                slot.Frame = -1;
                RoomSound.StopAllSounds(game);
            }
        }
        return emptyCount;
    }

    /// <remarks>C: FireworkState (wcdata.h).</remarks>
    private struct Slot
    {
        public short Frame;
        public short X;
        public short Y;
        public short Variant;
    }
}
