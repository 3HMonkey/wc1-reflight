namespace WingCommander.Game.Flight;

/// <summary>
/// Presentation switches of the flight layer (ADR-012). Every option only changes pixels or
/// sound, never the simulation or the shared random sequence. The defaults are the "fixed"
/// behaviour (DOS look or SDL port); <c>false</c> gives the literal Kilrathi Saga build.
/// </summary>
public sealed class FlightOptions
{
    /// <summary>
    /// Background planets are drawn as their own scaled and rolled sprites (SDL port, "WCDX fix",
    /// as the DOS game shows them). Off: the Kilrathi Saga draws a planet as constellation frame
    /// 0, a dust dot.
    /// </summary>
    /// <remarks>C: the OBJECT_CLASS_PLANET case of draw_sorted_objects_to_buffer and
    /// intro_drawbackgroundships (eventmgr.c, #ifdef SDL_PORT).</remarks>
    public bool DrawPlanets { get; set; } = true;

    /// <summary>
    /// Knocked-out VDUs show static noise (the SDL port's xorshift static, its own generator,
    /// never the game's random numbers) and play the DOS static sound 23. Off: the Kilrathi Saga
    /// with DOS data draws nothing and stays silent.
    /// </summary>
    /// <remarks>C: snow_viewport (gr.c) and PlaySnowStaticSound (sound.c) from malf_noise (cockpt.c).</remarks>
    public bool VduStaticNoise { get; set; } = true;

    /// <summary>
    /// A hit that damages the cockpit plays the eight-frame cockpit explosion before the damage
    /// decal appears (the DOS behaviour). Off: the Kilrathi Saga frees the explosion shape before
    /// the first hit, so decals appear instantly.
    /// </summary>
    /// <remarks>C: cockpit_explosion / place_damage_on_cockpit (cockpt.c).</remarks>
    public bool CockpitExplosionAnimation { get; set; } = true;

    /// <summary>
    /// Esc pauses a campaign flight like P (SDL port). Off: Esc is inert in campaign flight. In the
    /// training simulator Esc always ends the flight.
    /// </summary>
    /// <remarks>C: the 0x01 case of HandleSpaceFlightControls (hudmsg.c, #ifdef SDL_PORT).</remarks>
    public bool EscapePausesFlight { get; set; } = true;

    /// <summary>
    /// The attract mode shows the 11 DOS credit cards, or 19 with the Saga option, computed once.
    /// Off: the Kilrathi Saga adds 9 to the count on every visit of the title (reading past the
    /// table), which the port clamps to the 19 existing cards.
    /// </summary>
    /// <remarks>C: nIntroCreditCount in Title_Sequence (nav.c).</remarks>
    public bool FixedCreditCount { get; set; } = true;

    /// <summary>
    /// Space objects go to an R2-capable renderer as sprites at output resolution (ADR-010), drawn
    /// under the classic cockpit and HUD; the classic frame then shows the space colour where
    /// they are. Takes effect only with <see cref="RendererSupportsSpaceSprites"/>. Off: every
    /// sprite is drawn by the CPU into the classic frame (the reference path).
    /// </summary>
    /// <remarks>C: SdlBeginSpaceFrame / SdlRecordSpaceSprite / SdlCompleteSpaceFrame (sdl/gl_renderer.c).</remarks>
    public bool SpriteSpaceView { get; set; } = true;

    /// <summary>
    /// Set by the host when its renderer draws <c>RenderFrame.Space</c> sprites
    /// (<c>VulkanRenderer.SupportsSpaceSprites</c>); the SDL_Renderer fallback and headless hosts
    /// leave it off, and the CPU then draws every sprite.
    /// </summary>
    public bool RendererSupportsSpaceSprites { get; set; }
}
