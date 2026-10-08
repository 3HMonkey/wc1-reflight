using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class DamageTests
{
    /// <summary>Impact directions (relative motion of the projectile) for a ship facing +z.</summary>
    private static readonly FixedVector FromFront = new(0, 0, -0x100);
    private static readonly FixedVector FromBehind = new(0, 0, 0x100);
    private static readonly FixedVector MovingRight = new(0x100, 0, 0);
    private static readonly FixedVector MovingLeft = new(-0x100, 0, 0);

    [Fact]
    public void Shields_absorb_then_the_armor_of_the_hit_side()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        short ship = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 1000); // shields 50/50, armor 45/35/30/30
        uint seed = sim.Random.Seed;

        Assert.False(sim.InflictDamage(-1, ship, 20, FromFront));
        Assert.Equal(30, sim.Ships[ship].Shield[ShieldValues.Fore]);
        Assert.False(sim.InflictDamage(-1, ship, 20, FromBehind));
        Assert.Equal(30, sim.Ships[ship].Shield[ShieldValues.Aft]);

        // 40 through the 30 left of the fore shield: the front armor takes 10.
        sim.InflictDamage(-1, ship, 40, FromFront);
        Assert.Equal(0, sim.Ships[ship].Shield[ShieldValues.Fore]);
        Assert.Equal(35, sim.Ships[ship].Armor[ArmorValues.Front]);

        // Without shields a hit moving along +right takes armor 3, along -right armor 2.
        sim.Ships[ship].Shield[ShieldValues.Aft] = 0;
        var rightAndBack = new FixedVector(0xb0, 0, 0xc0);
        sim.InflictDamage(-1, ship, 5, MovingRight);
        Assert.Equal(25, sim.Ships[ship].Armor[ArmorValues.Right]);
        sim.InflictDamage(-1, ship, 5, MovingLeft);
        Assert.Equal(25, sim.Ships[ship].Armor[ArmorValues.Left]);
        // Below the 0.707 threshold the fore/aft armor is hit.
        sim.InflictDamage(-1, ship, 5, rightAndBack);
        Assert.Equal(30, sim.Ships[ship].Armor[ArmorValues.Rear]);

        // None of this needs a random number; projectile sounds only for projectile attackers.
        Assert.Equal(seed, sim.Random.Seed);
        Assert.Equal(0, events.Count("sfx"));
    }

    [Fact]
    public void Projectile_hits_sound_by_layer()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 1000);
        short bolt = sim.NewObject(ObjectType.LaserCannon, 0);
        sim.InflictDamage(bolt, ship, 20, FromFront);
        sim.InflictDamage(bolt, ship, 40, FromFront);
        Assert.Equal(new[] { $"sfx 10 {ship}", $"sfx 9 {ship}" }, events.Calls.Where(c => c.StartsWith("sfx", StringComparison.Ordinal)));
    }

    [Fact]
    public void Damage_through_the_armor_becomes_internal_damage_events()
    {
        var sim = TestWorld.Create(seed: FindSeed(peek => peek.BelowOrEqual(99) != 0));
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Objects[ship].ScreenX = ObjectSlots.NotVisible; // no hull debris roll
        sim.Ships[ship].Shield[ShieldValues.Fore] = 0;
        sim.Ships[ship].Armor[ArmorValues.Front] = 0;
        sim.InflictDamage(-1, ship, 60, FromFront);
        // An unrated pilot gets damage / 6 events.
        Assert.Equal(10, sim.Objects[ship].AccumulatedDamage);
    }

    [Fact]
    public void Rated_pilots_get_fewer_internal_damage_events()
    {
        var sim = TestWorld.Create(seed: FindSeed(peek => peek.BelowOrEqual(99) != 0));
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000, rating: 2);
        sim.Objects[ship].ScreenX = ObjectSlots.NotVisible;
        sim.Ships[ship].Shield[ShieldValues.Fore] = 0;
        sim.Ships[ship].Armor[ArmorValues.Front] = 0;
        var peek = TestWorld.Peek(sim);
        peek.BelowOrEqual(99);
        int cap = peek.InRange(3, 4);
        sim.InflictDamage(-1, ship, 200, FromFront);
        // min(RandomInRange(3, 4), damage / 40) events; the last one is always core damage.
        Assert.Equal(Math.Min(cap, 5), sim.Objects[ship].AccumulatedDamage);
        Assert.True(sim.Ships[ship].Damage >= 1);
    }

    [Theory]
    [InlineData(ObjectClass.Projectile, 48, 3)]
    [InlineData(ObjectClass.Asteroid, 256, 2)]
    [InlineData(ObjectClass.Missile, 96, 3)]
    public void Player_internal_damage_events_depend_on_the_attacker(ObjectClass attackerClass, short damage, short events)
    {
        var recording = new RecordingEvents();
        var sim = TestWorld.Create(seed: FindSeed(peek => peek.BelowOrEqual(99) != 0), events: recording);
        TestWorld.AddPlayer(sim);
        sim.Objects[0].ScreenX = ObjectSlots.NotVisible;
        sim.Ships[0].Shield[ShieldValues.Fore] = 0;
        sim.Ships[0].Armor[ArmorValues.Front] = 0;
        short attacker = sim.FindVacant3dObject();
        sim.Objects[attacker].Class = attackerClass;
        sim.InflictDamage(attacker, 0, damage, FromFront);
        Assert.Equal(events, sim.Objects[0].AccumulatedDamage);
        Assert.Equal(1, recording.Count("playerHit"));
        Assert.Equal(events > 1 ? 1 : 0, recording.Count("cockpitDamage"));
    }

    [Fact]
    public void Kilrathi_capital_ships_count_damage_events_and_burn()
    {
        var sim = TestWorld.Create(seed: FindSeed(peek => peek.BelowOrEqual(99) != 0));
        short fralthi = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Kilrathi, 0, 0, 3000); // capacity 110
        sim.Ships[fralthi].Shield[ShieldValues.Fore] = 0;
        sim.Ships[fralthi].Armor[ArmorValues.Front] = 0;
        sim.InflictDamage(-1, fralthi, 80, FromFront);
        Assert.Equal(10, sim.Objects[fralthi].AccumulatedDamage); // damage / 8
        // An onboard explosion: EXPLOSION2 at four times its scale for 6 frames, moving with the ship.
        ref var blast = ref sim.Objects[10];
        Assert.Equal(ObjectType.Explosion2, blast.Type);
        Assert.Equal(256 << 2, blast.Scale);
        Assert.Equal(6, blast.Counter);
        Assert.Equal(fralthi, blast.Owner);
    }

    [Fact]
    public void A_destroyed_capital_ship_burns_before_it_breaks_up()
    {
        var sim = TestWorld.Create(seed: FindSeed(peek => peek.BelowOrEqual(99) != 0));
        short fralthi = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Kilrathi, 0, 0, 3000);
        sim.Objects[fralthi].ScreenX = ObjectSlots.NotVisible;
        sim.Ships[fralthi].Shield[ShieldValues.Fore] = 0;
        sim.Ships[fralthi].Armor[ArmorValues.Front] = 0;
        sim.Objects[fralthi].AccumulatedDamage = 105;
        Assert.True(sim.InflictDamage(-1, fralthi, 80, FromFront));
        Assert.Equal(SpecialManeuver.Unknown9, sim.Ships[fralthi].SpecialManeuver);
        Assert.Equal(110 / 4 + 8, sim.Objects[fralthi].Counter);
        // Four onboard explosions, the ship slot stays a capital ship while it burns.
        Assert.Equal(4, CountType(sim, ObjectType.Explosion2));
        Assert.Equal(ObjectClass.CapitalShip, sim.Objects[fralthi].Class);
        // Dying ships take no further damage.
        Assert.False(sim.InflictDamage(-1, fralthi, 80, FromFront));
        Assert.False(sim.Explode(-1, fralthi));

        bool bigExplosion = false;
        for (int frame = 0; frame < 110 / 4 + 8; frame++)
        {
            for (short obj = ObjectSlots.FirstEffect; obj <= ObjectSlots.LastMoving; obj++)
                sim.AnimateObject(obj); // lets the onboard explosions run out
            sim.HouseKeepObjects();
            if (sim.Objects[fralthi].Counter == 7)
                bigExplosion = CountType(sim, ObjectType.Explosion1) == 1; // the final explosion at counter 7
        }
        Assert.True(bigExplosion);
        Assert.Equal(ObjectClass.Null, sim.Objects[fralthi].Class);
        Assert.Equal(7, CountClass(sim, ObjectClass.Debris));
    }

    [Fact]
    public void Named_pilots_survive_half_of_their_deaths()
    {
        uint survives = FindSeed(peek => peek.BelowOrEqual(1) == 0);
        var sim = TestWorld.Create(seed: survives);
        short ship = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 1000, rating: 2);
        Assert.False(sim.Explode(-1, ship));
        Assert.Equal(ObjectClass.Ship, sim.Objects[ship].Class);

        uint dies = FindSeed(peek => peek.BelowOrEqual(1) != 0);
        sim = TestWorld.Create(seed: dies);
        ship = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 1000, rating: 2);
        Assert.True(sim.Explode(-1, ship));
        Assert.Equal(SpecialManeuver.Unknown9, sim.Ships[ship].SpecialManeuver);
    }

    [Fact]
    public void A_kilrathi_ace_escapes_once()
    {
        var campaign = new SimulationCampaignState();
        var sim = TestWorld.Create();
        sim.Campaign = campaign;
        campaign.SetAceFlags(0, 0x21);
        short bhurak = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 1000, rating: 9);
        sim.Ships[bhurak].Damage = 5;
        Assert.False(sim.Explode(-1, bhurak));
        Assert.Equal(0x01, campaign.GetAceFlags(0));
        Assert.Equal(-25, sim.Ships[bhurak].Stress);
        Assert.Equal(ShipManeuver.OutaHere, sim.Ships[bhurak].Maneuver);
        Assert.Equal(2, sim.Ships[bhurak].Damage);
        Assert.Equal(6, sim.Ships[bhurak].WingmanMessageState);
    }

    [Fact]
    public void A_kill_scores_and_turns_the_fighter_into_an_explosion()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short salthi = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 5000);
        sim.Objects[salthi].ScreenX = 0; // on screen: the explosion is heard
        Assert.True(sim.Explode(0, salthi));
        Assert.Equal(7, sim.Campaign.MissionScore);
        Assert.Equal(7, sim.MissionMedalScore);
        Assert.Equal(70, sim.ArcadeScore);
        Assert.Equal(1, sim.PlayerKillCount);
        Assert.Contains($"killMusic 0 {salthi}", events.Calls);
        Assert.Contains($"sfx 4 {salthi}", events.Calls);
        Assert.Equal(SpecialManeuver.Unknown9, sim.Ships[salthi].SpecialManeuver);
        Assert.Equal(8, sim.Objects[salthi].Counter);
        ref var explosion = ref sim.Objects[10];
        Assert.Equal(ObjectType.Explosion1, explosion.Type);
        Assert.Equal(salthi, explosion.Owner);
        Assert.Equal(sim.Objects[salthi].Position, explosion.Position);
        Assert.Equal(1024, explosion.Scale); // 256 scaled by the Salthi's 1024

        for (int frame = 0; frame < 8; frame++)
            sim.HouseKeepObjects();
        Assert.Equal(ObjectClass.Null, sim.Objects[salthi].Class);
        Assert.Equal(7, CountClass(sim, ObjectClass.Debris));
        Assert.Equal(8, CountType(sim, ObjectType.DebrisDust));
    }

    [Theory]
    [InlineData(ObjectType.Dralthi, 10)]
    [InlineData(ObjectType.Gratha, 15)]
    [InlineData(ObjectType.Fralthi, 50)]
    [InlineData(ObjectType.Sivar, 75)]
    public void Kills_score_by_type(ObjectType type, short points)
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short victim = TestWorld.AddShip(sim, type, Side.Kilrathi, 0, 0, 9000);
        sim.ScoreForKill(0, victim);
        Assert.Equal(points, sim.Campaign.MissionScore);
        Assert.Equal(points * 10, sim.ArcadeScore);
    }

    [Theory]
    [InlineData(750, 6)] // 6000 / 30 / 30
    [InlineData(880, 3)] // 6000 / 40 / 40
    [InlineData(1001, 0)] // beyond 1000
    [InlineData(300, 18)] // divisor find_ratio(0, 500, 300, 8, 25) = 18
    [InlineData(0, 93)] // divisor 8
    public void Shock_waves_fall_off_with_distance(int hullDistance, short expectedDamage)
    {
        var sim = TestWorld.Create();
        short blast = sim.FindVacant3dObject();
        sim.SetObjectsData(blast, ObjectType.Explosion2, -1);
        // A Gratha (radius 140, aft shield 95) behind the blast, facing away from it.
        short ship = TestWorld.AddShip(sim, ObjectType.Gratha, Side.Kilrathi, 0, 0, hullDistance + 140);
        short divisor = hullDistance > 750 ? (short)40 : hullDistance > 500 ? (short)30 : ScalarMath.FindRatio(0, 500, (short)hullDistance, 8, 25);
        Assert.Equal(hullDistance > 1000 ? 0 : 6000 / divisor / divisor, expectedDamage);
        sim.ExplosionShockWave(blast, 6000);
        int damage = 95 - sim.Ships[ship].Shield[ShieldValues.Aft];
        Assert.Equal(expectedDamage > 1 ? expectedDamage : 0, damage);
        // Pushed away: force damage << 8 over the mass 126 << 8.
        int push = expectedDamage > 1 ? FixedMath.Divide(expectedDamage << 8, 126 << 8) : 0;
        Assert.Equal(new FixedVector(0, 0, push), sim.Objects[ship].Velocity);
    }

    [Fact]
    public void The_player_takes_three_quarters_of_a_shock_wave()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short blast = sim.FindVacant3dObject();
        sim.SetObjectsData(blast, ObjectType.Explosion2, -1);
        sim.Objects[blast].Position = TestWorld.Units(0, 0, -(100 + 750)); // behind the player: 750 from the hull
        sim.ExplosionShockWave(blast, 6000);
        Assert.Equal(40 - 4, sim.Ships[0].Shield[ShieldValues.Aft]); // 6 * 3 / 4 on the aft shield
        Assert.Equal(40, sim.Ships[0].Shield[ShieldValues.Fore]);
    }

    [Fact]
    public void Component_damage_and_malfunctions()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        Assert.False(sim.Malf(0));
        Assert.Equal(2, sim.DamageYourComponent(0, 2, 3));
        Assert.Equal(3, sim.DamageYourComponent(0, 2, 3));
        Assert.Equal(3, sim.PlayerComponentDamage[0]);
        Assert.Contains("hud ComponentHit 0", events.Calls);

        // A component at 4 malfunctions on every roll: rand() % 16 < 16.
        sim.PlayerComponentDamage[5] = 4;
        Assert.True(sim.Malf(5));
        // The ejector: intact it works half of the time (RandomInRange(0, 0) returns 0 or 1).
        sim.PlayerComponentDamage[7] = 4;
        sim.TryEject();
        Assert.Equal(0, sim.ArcadeState);
    }

    [Fact]
    public void Damage_level_for_the_landing_scene()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        Assert.Equal(0, sim.CalculateDamageLevel());
        sim.Ships[0].Armor[ArmorValues.Front] = 0; // 4 * 2 = 8
        Assert.Equal(1, sim.CalculateDamageLevel());
        sim.Ships[0].Damage = 3; // + 3 * 30 / 5 = 18
        sim.Objects[0].AccumulatedDamage = 2; // + 10: 36
        Assert.Equal(1, sim.CalculateDamageLevel());
        sim.Objects[0].AccumulatedDamage = 3; // 41
        Assert.Equal(2, sim.CalculateDamageLevel());
        sim.Objects[0].AccumulatedDamage = 9; // 71
        Assert.Equal(3, sim.CalculateDamageLevel());
    }

    private static int CountType(SpaceSimulation sim, ObjectType type) =>
        Enumerable.Range(0, ObjectSlots.Count).Count(i => sim.Objects[i].Class != ObjectClass.Null && sim.Objects[i].Type == type);

    private static int CountClass(SpaceSimulation sim, ObjectClass objectClass) =>
        Enumerable.Range(0, ObjectSlots.Count).Count(i => sim.Objects[i].Class == objectClass);

    /// <summary>The first seed from 1 for which <paramref name="condition"/> holds on a fresh generator.</summary>
    private static uint FindSeed(Func<CRandom, bool> condition)
    {
        for (uint seed = 1; ; seed++)
        {
            if (condition(new CRandom(seed)))
                return seed;
        }
    }
}
