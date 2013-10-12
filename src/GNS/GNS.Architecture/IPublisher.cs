namespace GNS.Architecture
{
    public interface IPublisher
    {
        void Subscribe(IRecipient recipient, IRule rule);
        void Unsubscribe(IRecipient recipient, IRule rule);
        void Unsubscribe(IRecipient recipient);
    }
}