using WingCommander.Core.Resources;

namespace WingCommander.Core.Tests.Resources;

using WingCommander.Tests;

public class PacketFileTests
{
    [DataFact]
    public void GameDirectory_detects_dos_data()
    {
        var dir = GameData.Require();
        Assert.True(dir.IsDosData);
        Assert.True(dir.Exists("module.000"), "case-insensitive lookup");
    }

    [DataFact]
    public void InstallTable_maps_well_known_logical_files()
    {
        var table = GameData.Require().InstallTable;
        Assert.Equal("FONTS.FNT", table[LogicalFile.Fonts].Name);
        Assert.Equal("TITLE.VGA", table[LogicalFile.TitleVga].Name);
        Assert.Equal("MODULE.000", table[LogicalFile.Module000].Name);
        Assert.Equal("CAMP.000", table[LogicalFile.Camp000].Name);
        Assert.Equal("OBJECTS.VGA", table[LogicalFile.ObjectsVga].Name);
        Assert.Equal("SERIES.VGA", table[LogicalFile.SeriesVga].Name);
        Assert.Equal("MODULE.002", table[LogicalFile.Module002].Name);
        Assert.Equal("TITLE1.VGA", table[LogicalFile.Title1Vga].Name);
        Assert.False(table.TryGet(76, out _));
        Assert.Equal(73, table.Records.Count);
    }

    [DataFact]
    public void Module000_has_six_lzw_sections_that_decompress()
    {
        var packet = GameData.Require().OpenPacket("MODULE.000");
        Assert.Equal(6, packet.SectionCount);
        for (int i = 0; i < packet.SectionCount; i++)
        {
            Assert.Equal(PacketCompression.Lzw, packet.GetInfo(i).Compression);
            var decoded = packet.GetSection(i);
            Assert.Equal(packet.GetDecodedSize(i), decoded.Length);
            Assert.True(decoded.Length > 0);
        }
    }

    [DataTheory]
    [InlineData("TITLE.VGA")]
    [InlineData("FONTS.FNT")]
    [InlineData("OBJECTS.VGA")]
    [InlineData("COCKPIT.VGA")]
    [InlineData("MUSIC.MID")]
    [InlineData("CAMP.000")]
    [InlineData("BRIEFING.000")]
    [InlineData("SHIPTYPE.V00")]
    [InlineData("PCSHIP.V00")]
    public void Every_section_of_common_files_decodes(string fileName)
    {
        var packet = GameData.Require().OpenPacket(fileName);
        Assert.True(packet.SectionCount > 0);
        for (int i = 0; i < packet.SectionCount; i++)
        {
            var decoded = packet.GetSection(i);
            Assert.Equal(packet.GetDecodedSize(i), decoded.Length);
        }
    }

    [Fact]
    public void Lzw_rejects_truncated_stream()
    {
        // clear code (0x100) as 9 bits LSB-first then nothing: 0x00 0x01
        byte[] data = [0x00, 0x01];
        Assert.Throws<GameDataException>(() => OriginLzw.Decompress(data, new byte[4]));
    }
}
