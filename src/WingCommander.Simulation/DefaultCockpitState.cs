namespace WingCommander.Simulation;

/// <summary>A fixed <see cref="ICockpitState"/> for tests and tools; starts like <c>init_vdus</c>
/// outside the training simulator (left VDU weapons, right VDU navigation).</summary>
/// <remarks>C: init_vdus (logic.c): set_mode(0, 1), set_mode(1, nTrainSimActive ? 3 : 5).</remarks>
public sealed class DefaultCockpitState : ICockpitState
{
    /// <summary>Modes of the left and right VDU.</summary>
    public int[] VduModes { get; } = [1, 5];

    public int GetVduMode(int vdu) => VduModes[vdu];

    /// <summary>Value returned by <see cref="MessageShowing"/>.</summary>
    public bool MessageActive { get; set; }

    public bool MessageShowing() => MessageActive;
}
