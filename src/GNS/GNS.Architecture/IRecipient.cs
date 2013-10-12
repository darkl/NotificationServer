namespace GNS.Architecture
{
    public interface IRecipient
    {
        void HandleNotification(Notification notification);
    }
}