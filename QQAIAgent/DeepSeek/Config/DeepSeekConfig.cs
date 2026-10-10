namespace DeepSeek.Config;

public sealed class DeepSeekConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    #endregion
}
