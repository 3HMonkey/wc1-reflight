namespace WingCommander.Simulation.Data;

/// <summary>
/// Wingman/comm order codes passed to <c>request(requester, ship, command)</c>; the index into
/// <c>apszCommMenuText</c> (0x0046AF90).
/// </summary>
/// <remarks>C: enum CommMenuEntry (include/wcdata.h, first six) and apszCommMenuText (globals.c).</remarks>
public enum CommCommand
{
    /// <summary>"Never mind..."</summary>
    NeverMind = 0,
    /// <summary>"Attack my target!"</summary>
    AttackTarget = 1,
    /// <summary>"Help me out here"</summary>
    HelpMeOut = 2,
    /// <summary>"Return to base."</summary>
    ReturnToBase = 3,
    /// <summary>"Die furball!"</summary>
    DieFurball = 4,
    /// <summary>"Slag off!"</summary>
    SlagOff = 5,
    /// <summary>"Bite it cat face."</summary>
    BiteItCatFace = 6,
    /// <summary>"Break and attack."</summary>
    BreakAndAttack = 7,
    /// <summary>"Keep formation!"</summary>
    KeepFormation = 8,
    /// <summary>"Form on my wing."</summary>
    FormOnMyWing = 9,
    /// <summary>"Keep radio silence"</summary>
    KeepRadioSilence = 10,
    /// <summary>"Broadcast freely"</summary>
    BroadcastFreely = 11,
    /// <summary>"Request Landing"</summary>
    RequestLanding = 12,
}
