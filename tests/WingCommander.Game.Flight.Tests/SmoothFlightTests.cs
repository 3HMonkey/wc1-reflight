using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Simulation.Data;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>R2b (ADR-019): the space view between the 20 Hz simulation ticks.</summary>
public class SmoothFlightTests
{
    private static void Hold(FlightRig rig, int scanCode, int virtualKey, double down, double up)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), down);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), up);
    }

    [DataFact]
    public void At_144_hz_a_turn_moves_the_stars_on_every_display_frame_and_flames_stay_on_their_ships()
    {
        var rig = new FlightRig(3, new FlightOptions { RendererSupportsSpaceSprites = true });
        int frames = 0;
        rig.Start(async r =>
        {
            r.StopAfter(100, (session, count) =>
            {
                frames = count;
                if (count == 20)
                {
                    double now = r.Runtime.Scheduler.Now;
                    Hold(r, 0x4B, 0x25, now + 1, now + 3_000); // Left arrow until frame 80, then the guns
                    Hold(r, 0x39, 0x20, now + 100, now + 2_000);
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });

        // The host loop at 144 Hz: every update moves the display on by 1/7.2 of a tick.
        var stars = new Dictionary<int, List<(int Display, float X, float Y)>>();
        SpaceView? checkedFrame = null;
        int attached = 0;
        var step = TimeSpan.FromMilliseconds(1000.0 / 144.0);
        for (int display = 0; !rig.Runtime.IsFinished && display < 144 * 120; display++)
        {
            rig.Runtime.Update(step);
            if (rig.Runtime.Failure is { } failure)
                throw new InvalidOperationException("The game coroutine failed.", failure);
            if (frames is < 40 or > 75 || rig.Runtime.Frame.Space is not { Current: { } state } space)
                continue;
            float t = rig.Runtime.Frame.Interpolation;
            var sprites = space.Sprites.Items;
            for (int i = 0; i < sprites.Length; i++)
            {
                ref readonly var sprite = ref sprites[i];
                int index = sprite.ObjectSlot < 0 ? -1 : state.IndexOfSlot(sprite.ObjectSlot);
                if (index < 0)
                    continue;
                ref readonly var o = ref state.Objects[index];
                if (o.Class == (short)ObjectClass.Star && sprite.HasPrevious)
                {
                    var shown = sprite.At(t);
                    (stars.TryGetValue(o.SpawnId, out var track) ? track : stars[o.SpawnId] = []).Add((display, shown.X, shown.Y));
                }
                if (o.Class == (short)ObjectClass.FixedObject && !ReferenceEquals(space, checkedFrame))
                {
                    // Engine flames and turrets keep their offset to the parent, turned and scaled with it.
                    int p = 0;
                    while (p < sprites.Length && sprites[p].ObjectSlot != o.Owner)
                        p++;
                    if (p == sprites.Length || !sprites[p].HasPrevious)
                        continue;
                    ref readonly var parent = ref sprites[p];
                    Assert.True(sprite.HasPrevious, $"fixed child {sprite.ObjectSlot} of {o.Owner} is not attached");
                    float now = MathF.Sqrt(MathF.Pow(sprite.X - parent.X, 2) + MathF.Pow(sprite.Y - parent.Y, 2));
                    float before = MathF.Sqrt(MathF.Pow(sprite.PreviousX - parent.PreviousX, 2) + MathF.Pow(sprite.PreviousY - parent.PreviousY, 2));
                    Assert.Equal(now * parent.PreviousScale / parent.Scale, before, 2);
                    attached++;
                }
            }
            checkedFrame = space;
        }
        Assert.Equal(100, frames);
        Assert.True(attached > 0, "no engine flames or turrets were checked");

        var tracks = stars.Values.Where(track => track.Count >= 50).ToList();
        Assert.True(tracks.Count >= 2, $"only {tracks.Count} stars stayed on screen");
        foreach (var track in tracks)
        {
            var steps = new List<double>();
            for (int i = 1; i < track.Count; i++)
            {
                if (track[i].Display == track[i - 1].Display + 1)
                    steps.Add(Math.Sqrt(Math.Pow(track[i].X - track[i - 1].X, 2) + Math.Pow(track[i].Y - track[i - 1].Y, 2)));
            }
            double mean = steps.Average();
            Assert.True(mean > 1, $"the stars barely moved ({mean:0.00} px per display frame)");
            // Drawn only at the ticks they would stand still for six frames and jump on the seventh.
            Assert.All(steps, s => Assert.InRange(s, mean * 0.25, mean * 3));
        }
    }
}
