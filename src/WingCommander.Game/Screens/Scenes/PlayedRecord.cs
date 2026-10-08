namespace WingCommander.Game.Screens.Scenes;

/// <summary>A conversation record whose shot handler ran (diagnostics and tests).</summary>
/// <param name="Index">Record index in the scene script.</param>
/// <param name="Shot">The record's shot (overlay bit removed; -1 = keep the previous shot).</param>
/// <param name="Handler">The shot whose handler ran (the previous shot for -1 and unknown shots).</param>
/// <param name="StartMilliseconds">Virtual time the handler started.</param>
public readonly record struct PlayedRecord(int Index, int Shot, int Handler, double StartMilliseconds);
