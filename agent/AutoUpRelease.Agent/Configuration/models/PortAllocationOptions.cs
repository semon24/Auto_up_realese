namespace AutoUpRelease.Agent;

public class PortAllocationOptions
{
    public List<string> Keys { get; set; } = new();
    public string KeysCsv { get; set; } = "";
    public int ScanMin { get; set; } = 1024;
    public int ScanMax { get; set; } = 65535;
}
