# Fonts and lettering

Status 2026-10-08. Basis for output-resolution text and replacement fonts (ADR-013).
Sample sheets with in-game examples can be generated locally with `wc1tool hd-text` and
`wc1tool snap --hd` (they are derived from the game data and never committed).

## FONTS.FNT (logical file 0, four sections)

Proportional bitmap fonts, one palette index per pixel (`BitmapFont`): ink pixels take the text
colour, background pixels the background colour (0xFF = transparent), other values are fixed
colours. All text drawing goes through `GraphicsContext.DrawFontGlyph`.

| Font | Height | Ink / bg | Characters | Used for (C# text contexts) |
| --- | --- | --- | --- | --- |
| 0 | 11 px, bold | 15 / 0 | printable ASCII 0x20-0x7E | conversation subtitles (`ConversationStage.Text`, scramble radio `_conversationTextContext`), modal prompts and messages (`ModalPrompts`: "Game Name:", "Loading Game...", Y/N questions), room hover labels (`RoomMenu.LabelContext`), medal ceremony (`MedalsView`) |
| 1 | 8 px | 166 / 0 | printable ASCII | default text context (`Wc1Game.DefaultText`), HUD messages (`HudMessageTextContext`, red), nav map texts (`_navMapTextContext`), briefing map readout, training simulator lists and name entry (`TrainSim`), modal panels with font -1; resident (never released) |
| 2 | 6 px | 198 / 0 | printable ASCII + symbols at 0xF0-0xFF | cockpit VDUs (`LeftVduTextContext`, `RightVduTextContext`: weapons, damage, target, navigation, comm menu), speed readouts (`CockpitReadoutTextContext`: KPS / SET), nav point labels on nav and briefing maps |
| 3 | 11 px, chalk | 15 / 1 | `-`, `.`, digits, upper-case letters (other codes hold garbage offsets) | rec room chalk board / kill board (`ChalkBoard`); grey shading pixels are fixed colours |

The briefing room shows the nav map scaled down from an off-screen buffer, so its text is not
drawn on the screen directly (stays classic in the output-resolution text layer).

## Lettering that is not a font

Painted into the art and drawn as shapes, not replaceable by fonts: cockpit labels (EJECT,
BLASTER, LOCK, AUTO, SHIELDS, FUEL), "Get Ready" / "Victory" captions of the simulator, the
title logo, room signs, the Origin intro and attract credits (a shape font, `introFont`, drawn
with `DrawCenteredScaledIntroText`).

## Output-resolution glyphs

`GlyphImageBuilder` (Graphics) vectorizes each glyph with `PixelOutline` (Core.Imaging): pixel
boundaries traced 8-connected, one-pixel staircase steps replaced by diagonals, one-pixel caps,
inner corners of thin crosses and corners between longer runs kept square; stored as a signed
distance field at 8 texels per pixel plus per-texel colours (`GlyphImage`). Multicolour glyphs
(font 3, font 2 symbols) blend the colours of the four nearest source pixels. `wc1tool hd-text
<font> ["text"] --scale N --aspect` renders classic vs. output-resolution glyphs for comparison.

## Replacement fonts

TrueType files replace fonts 0-3 glyph by glyph (`FontReplacement`, bundled files in
`assets/fonts/`, credited in README.md): the game keeps laying text out with the original
advances (word wrap, centring, VDU layouts), so every replacement glyph is fitted into the
original glyph cell: the original upper-case letters give the cap height and the baseline, one
font-wide horizontal factor (the median that makes the glyphs fit their original ink width)
condenses the design, and glyphs that would still overflow are condensed further. Measured
factors: Tektur SemiBold in font 0: 0.90; SPACE WING LEADER in font 1: 0.74, in font 2: 0.83;
CHAWP in font 3: 1.0 (narrower than the original, centred). Characters a replacement lacks and
the font 2 symbols (0xF2-0xFF) keep the vectorized original.
