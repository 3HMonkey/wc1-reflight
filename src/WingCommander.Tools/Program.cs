using WingCommander.Core.Resources;
using WingCommander.Tools;

try
{
    return Commands.Run(args);
}
catch (GameDataException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}
