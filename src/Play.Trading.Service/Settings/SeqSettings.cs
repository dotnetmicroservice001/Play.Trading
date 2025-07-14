namespace Play.Trading.Service.Settings;

public class SeqSettings
{
    public string Host { get; init; }
    public int Port { get; init; }

    public string ServerUrl => $"http://{Host}:{Port}";
    
    
}