using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent.Drive;

public interface IRelayPointerTransport
{
    Task JoinRoomAsync(string roomId, string roomSecret);
    Task PublishPointerAsync(ClipboardPointerPublish publish);
}


