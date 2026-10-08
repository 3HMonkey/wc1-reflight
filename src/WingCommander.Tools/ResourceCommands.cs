using System.Text;
using WingCommander.Core.Resources;

namespace WingCommander.Tools;

internal static partial class Commands
{
    static partial void RegisterResourceCommands()
    {
        Register("dump", "<file> [--nested]  list packet sections (decoded sizes, flags)", Dump);
        Register("hex", "<file> <sec[/sub..]> [--length N]  hex dump of a decoded section", Hex);
        Register("install", "print the INSTALL.DAT logical file table", Install);
    }

    private static int Dump(ToolOptions o)
    {
        var packet = OpenPacket(o, o.Positional(0));
        DumpPacket(packet, o.Has("nested"), 0);
        return 0;
    }

    private static void DumpPacket(PacketFile packet, bool nested, int indent)
    {
        string pad = new(' ', indent);
        Console.WriteLine($"{pad}{packet.Name}: {packet.SectionCount} sections, declared size {packet.DeclaredSize}");
        for (int i = 0; i < packet.SectionCount; i++)
        {
            var info = packet.GetInfo(i);
            int decodedSize;
            string status;
            try
            {
                decodedSize = packet.GetSection(i).Length;
                status = "";
            }
            catch (GameDataException e)
            {
                decodedSize = -1;
                status = $"  ERROR: {e.Message}";
            }
            string nestedMark = decodedSize >= 8 && packet.IsNestedPacket(i) ? " [packet]" : "";
            Console.WriteLine($"{pad}  [{i,3}] offset 0x{info.Offset:x6} stored {info.StoredSize,8} flags 0x{info.Flags:x2} decoded {decodedSize,8}{nestedMark}{status}");
            if (nested && nestedMark.Length != 0)
                DumpPacket(packet.OpenNested(i), nested, indent + 4);
        }
    }

    private static int Hex(ToolOptions o)
    {
        var packet = OpenPacket(o, o.Positional(0));
        string path = o.Positional(1);
        ReadOnlyMemory<byte> data = ResolveSectionPath(packet, path);
        int length = o.IntOption("length", 256);
        var span = data.Span[..Math.Min(length, data.Length)];
        Console.WriteLine($"{packet.Name} section {path}: {data.Length} bytes");
        var sb = new StringBuilder();
        for (int row = 0; row < span.Length; row += 16)
        {
            sb.Clear();
            sb.Append($"{row:x6}: ");
            for (int c = 0; c < 16; c++)
            {
                if (row + c < span.Length) sb.Append($"{span[row + c]:x2}");
                else sb.Append("  ");
                if ((c & 1) == 1) sb.Append(' ');
            }
            sb.Append(' ');
            for (int c = 0; c < 16 && row + c < span.Length; c++)
            {
                byte b = span[row + c];
                sb.Append(b is >= 0x20 and < 0x7f ? (char)b : '.');
            }
            Console.WriteLine(sb.ToString());
        }
        return 0;
    }

    private static int Install(ToolOptions o)
    {
        var dir = RequireGameDirectory(o);
        var table = dir.InstallTable;
        Console.WriteLine($"{"idx",3} {"id",3} {"name",-13} disk class present");
        for (int i = 0; i < InstallTable.SlotCount; i++)
        {
            if (!table.TryGet(i, out var r))
                continue;
            Console.WriteLine($"{i,3} {r.LogicalFileId,3} {r.Name,-13} {r.DiskNumber,4} {r.FileClass,5} {(dir.Exists(r.Name) ? "yes" : "NO")}");
        }
        Console.WriteLine();
        Console.WriteLine("Records without logical id:");
        foreach (var r in table.Records.Where(r => r.LogicalFileId == 0xff))
            Console.WriteLine($"    {r.Name,-13} disk {r.DiskNumber} class {r.FileClass}");
        return 0;
    }
}
