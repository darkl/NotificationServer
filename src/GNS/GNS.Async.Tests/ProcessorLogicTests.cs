namespace GNS.Async.Tests;

[TestFixture]
public class ProcessorLogicTests
{
    [Test]
    public async Task ProcessEventAsync_WithMatchingVoidHandler_InvokesHandler()
    {
        var logic = new TestProcessorLogic();
        var sampleEvent = new SampleEvent { Value = "Test123" };

        var result = await logic.ProcessEventAsync(sampleEvent, CancellationToken.None);

        Assert.IsTrue(logic.VoidHandlerCalled);
        Assert.AreEqual("Test123", logic.LastValue);
        Assert.IsNull(result); // Void handler returns null
    }

    [Test]
    public async Task ProcessEventAsync_WithCancellationToken_PassesTokenToHandler()
    {
        var logic = new TestProcessorLogic();
        var sampleEvent = new SampleEvent { Value = "Test123" };
        var cancellationToken = new CancellationToken();

        var result = await logic.ProcessEventAsync(sampleEvent, cancellationToken);

        Assert.IsTrue(logic.VoidHandlerCalled);
        Assert.AreEqual("Test123", logic.LastValue);
        Assert.IsNull(result);
    }

    public class TestProcessorLogic : ProcessorLogic
    {
        public SampleEvent LastProcessedEvent;
        public string LastValue;
        public bool VoidHandlerCalled;
        public bool AsyncHandlerCalled;

        public TestProcessorLogic() : base("TestLogic") { }

        [Processor]
        private void HandleSampleEvent(SampleEvent e)
        {
            LastProcessedEvent = e;
            LastValue = e.Value;
            VoidHandlerCalled = true;
        }

        [Processor]
        private IEvent HandleDerivedSampleEvent(DerivedSampleEvent e)
        {
            return new ResponseEvent { Response = "Handled Derived Event" };
        }

        [Processor]
        private async Task<IEvent> HandleAsyncSampleEvent(AsyncSampleEvent e)
        {
            AsyncHandlerCalled = true;
            await Task.Delay(1); // Simulate async work
            return new ResponseEvent { Response = "Handled Async Event" };
        }

        [Processor]
        private async Task HandleVoidAsyncSampleEvent(VoidAsyncSampleEvent e, CancellationToken cancellationToken)
        {
            AsyncHandlerCalled = true;
            await Task.Delay(1, cancellationToken); // Simulate async work
        }
    }

    [Test]
    public async Task ProcessEventAsync_WithDerivedTypeHandler_ReturnsExpectedResponse()
    {
        var logic = new TestProcessorLogic();
        var derivedEvent = new DerivedSampleEvent();

        var result = await logic.ProcessEventAsync(derivedEvent, CancellationToken.None) as ResponseEvent;

        Assert.IsNotNull(result);
        Assert.AreEqual("Handled Derived Event", result.Response);
    }

    [Test]
    public async Task ProcessEventAsync_WithAsyncHandler_ReturnsExpectedResponse()
    {
        var logic = new TestProcessorLogic();
        var asyncEvent = new AsyncSampleEvent();

        var result = await logic.ProcessEventAsync(asyncEvent, CancellationToken.None) as ResponseEvent;

        Assert.IsNotNull(result);
        Assert.AreEqual("Handled Async Event", result.Response);
        Assert.IsTrue(logic.AsyncHandlerCalled);
    }

    [Test]
    public async Task ProcessEventAsync_WithVoidAsyncHandler_ReturnsNull()
    {
        var logic = new TestProcessorLogic();
        var voidAsyncEvent = new VoidAsyncSampleEvent();

        var result = await logic.ProcessEventAsync(voidAsyncEvent, CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsTrue(logic.AsyncHandlerCalled);
    }

    [Test]
    public async Task ProcessEventAsync_WithNoMatchingHandler_ReturnsNull()
    {
        var logic = new TestProcessorLogic();
        var unknownEvent = new UnknownEvent();

        var result = await logic.ProcessEventAsync(unknownEvent, CancellationToken.None);

        Assert.IsNull(result);
    }

    [Test]
    public async Task ProcessEventAsync_WithNullEvent_ReturnsNull()
    {
        var logic = new TestProcessorLogic();

        var result = await logic.ProcessEventAsync(null, CancellationToken.None);

        Assert.IsNull(result);
    }

    public class SampleEvent : IEvent
    {
        public string Value { get; set; }
        public object Clone()
        {
            return new SampleEvent() { Value = Value };
        }
    }

    public class DerivedSampleEvent : SampleEvent { }

    public class AsyncSampleEvent : IEvent
    {
        public object Clone()
        {
            return new AsyncSampleEvent();
        }
    }

    public class VoidAsyncSampleEvent : IEvent
    {
        public object Clone()
        {
            return new VoidAsyncSampleEvent();
        }
    }

    public class ResponseEvent : IEvent
    {
        public string Response { get; set; }
        public object Clone()
        {
            return new ResponseEvent() { Response = Response };
        }
    }

    public class UnknownEvent : IEvent
    {
        public object Clone()
        {
            return new UnknownEvent();
        }
    }

    [TestFixture]
    public class ProcessorLogicValidationTests
    {
        [Test]
        public void ThrowsIfProcessorMethodHasInvalidReturnType()
        {
            // Method returns int — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new InvalidReturnTypeProcessorLogic("invalid-return");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasNoParameters()
        {
            // Method has 0 parameters — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new NoParameterProcessorLogic("no-params");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasTooManyParameters()
        {
            // Method has 3 parameters — invalid (max is 2: IEvent + CancellationToken)
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new TooManyParametersProcessorLogic("too-many-params");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasInvalidParameterType()
        {
            // First parameter is string — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new InvalidParameterTypeProcessorLogic("invalid-param");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasInvalidSecondParameterType()
        {
            // Second parameter is string instead of CancellationToken — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new InvalidSecondParameterTypeProcessorLogic("invalid-second-param");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasInvalidAsyncReturnType()
        {
            // Method returns Task<string> — invalid (should be Task<IEvent>)
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new InvalidAsyncReturnTypeProcessorLogic("invalid-async-return");
            });
        }

        [Test]
        public void ThrowsIfMultipleProcessorsForSameEventType()
        {
            // Two processors for the same event type — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new MultipleProcessorsForSameEventProcessorLogic("duplicate-processors");
            });
        }

        [Test]
        public void AllowsValidProcessorMethodSignatures()
        {
            // All these should be valid
            Assert.DoesNotThrow(() =>
            {
                var logic = new ValidProcessorMethodsLogic("valid-methods");
            });
        }
    }

    public class InvalidReturnTypeProcessorLogic : ProcessorLogic
    {
        public InvalidReturnTypeProcessorLogic(string name) : base(name) { }

        [Processor]
        private int InvalidReturn(IEvent e) => 42;
    }

    public class NoParameterProcessorLogic : ProcessorLogic
    {
        public NoParameterProcessorLogic(string name) : base(name) { }

        [Processor]
        private void NoParams() { }
    }

    public class TooManyParametersProcessorLogic : ProcessorLogic
    {
        public TooManyParametersProcessorLogic(string name) : base(name) { }

        [Processor]
        private void TooManyParams(IEvent e, CancellationToken token, string extra) { }
    }

    public class InvalidParameterTypeProcessorLogic : ProcessorLogic
    {
        public InvalidParameterTypeProcessorLogic(string name) : base(name) { }

        [Processor]
        private void NotAnEvent(string input) { }
    }

    public class InvalidSecondParameterTypeProcessorLogic : ProcessorLogic
    {
        public InvalidSecondParameterTypeProcessorLogic(string name) : base(name) { }

        [Processor]
        private void InvalidSecondParam(IEvent e, string notACancellationToken) { }
    }

    public class InvalidAsyncReturnTypeProcessorLogic : ProcessorLogic
    {
        public InvalidAsyncReturnTypeProcessorLogic(string name) : base(name) { }

        [Processor]
        private async Task<string> InvalidAsyncReturn(IEvent e) => await Task.FromResult("invalid");
    }

    public class MultipleProcessorsForSameEventProcessorLogic : ProcessorLogic
    {
        public MultipleProcessorsForSameEventProcessorLogic(string name) : base(name) { }

        [Processor]
        private void FirstProcessor(SampleEvent e) { }

        [Processor]
        private IEvent SecondProcessor(SampleEvent e) => e;
    }

    public class ValidProcessorMethodsLogic : ProcessorLogic
    {
        public ValidProcessorMethodsLogic(string name) : base(name) { }

        [Processor]
        private void VoidSync(SampleEvent e) { }

        [Processor]
        private void VoidSyncWithToken(DerivedSampleEvent e, CancellationToken token) { }

        [Processor]
        private IEvent ReturnSync(AsyncSampleEvent e) => e;

        [Processor]
        private async Task VoidAsync(ResponseEvent e) => await Task.CompletedTask;

        [Processor]
        private async Task VoidAsyncWithToken(UnknownEvent e, CancellationToken token) => await Task.CompletedTask;

        [Processor]
        private async Task<IEvent> ReturnAsyncWithToken(IEvent e, CancellationToken token) => await Task.FromResult(e);
    }
}