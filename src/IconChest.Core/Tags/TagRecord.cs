namespace IconChest.Core.Tags;

public sealed class TagRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FgColor { get; set; } = string.Empty;
    public string BgColor { get; set; } = string.Empty;
    public int ImageCount { get; set; }

    public TagRecord() { }

    public TagRecord(long id, string name, string fgColor, string bgColor, int ImageCount = 0)
    {
        Id = id;
        Name = name;
        FgColor = fgColor;
        BgColor = bgColor;
        this.ImageCount = ImageCount;
    }
}
