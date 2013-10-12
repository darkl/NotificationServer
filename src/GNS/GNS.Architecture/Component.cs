using System;

namespace GNS.Architecture
{
    public abstract class Component : StateDrivenEntity, IComponent, IRecipient, IPublisher
    {
        protected abstract void Consume(EventGroup eventGroup);

        protected void Publish(EventGroup eventGroup)
        {
            throw new NotImplementedException();
        }

        protected override void InnerUninitializedToInitialized()
        {
        }

        protected override void InnerInitializedToUninitialized()
        {
        }

        protected override void InnerInitializedToStarted()
        {
        }

        protected override void InnerStartedToInitialized()
        {
        }

        protected override void InnerAnyToInvalid()
        {
        }

        protected override void InnerInvalidToUnitialized()
        {
        }
        
        public void HandleNotification(Notification notification)
        {
            // Redirects the notification to the correct thread
            // that calls Consume
            throw new System.NotImplementedException();
        }

        public void Subscribe(IRecipient recipient, IRule rule)
        {
            throw new System.NotImplementedException();
        }

        public void Unsubscribe(IRecipient recipient, IRule rule)
        {
            throw new System.NotImplementedException();
        }

        public void Unsubscribe(IRecipient recipient)
        {
            throw new System.NotImplementedException();
        }

        public string UniqueName
        {
            get { throw new NotImplementedException(); }
        }

        public IRuntimeContext RuntimeContext
        {
            get; 
            set;
        }
    }
}