namespace QQAIAgent.Messaging;

public interface IMessageSender
{
    #region methods
    /// <summary>Sends a plain text group message and returns its message ID after the send API succeeds.</summary>
    Task<long> SendGroupMessageAsync(long groupId, string text, CancellationToken cancellationToken);
    #endregion
}
