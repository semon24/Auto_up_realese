namespace AutoUpRelease.Agent;

public class PortAllocationOptions
{
    public List<string> Keys { get; set; } = new();
    public int ScanMin { get; set; } = 1024;
    public int ScanMax { get; set; } = 65535;
}