using GNS.Architecture;

namespace GNS.Tests;

[TestFixture]
public class ProcessorLogicTests
{
    [Test]
    public void ProcessEvent_WithMatchingVoidHandler_InvokesHandler()
    {
        var logic = new TestProcessorLogic();
        var sampleEvent = new SampleEvent { Value = "Test123" };

        var result = logic.ProcessEvent(sampleEvent);

        Assert.IsTrue(logic.VoidHandlerCalled);
        Assert.AreEqual("Test123", logic.LastValue);
        Assert.IsNull(result); // Void handler returns null
    }

    public class TestProcessorLogic : ProcessorLogic
    {
        public SampleEvent LastProcessedEvent;
        public string LastValue;
        public bool VoidHandlerCalled;

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
    }

    [Test]
    public void ProcessEvent_WithDerivedTypeHandler_ReturnsExpectedResponse()
    {
        var logic = new TestProcessorLogic();
        var derivedEvent = new DerivedSampleEvent();

        var result = logic.ProcessEvent(derivedEvent) as ResponseEvent;

        Assert.IsNotNull(result);
        Assert.AreEqual("Handled Derived Event", result.Response);
    }

    [Test]
    public void ProcessEvent_WithNoMatchingHandler_ReturnsNull()
    {
        var logic = new TestProcessorLogic();

        var unknownEvent = new UnknownEvent();

        var result = logic.ProcessEvent(unknownEvent);

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
        public void ThrowsIfProcessorMethodHasMultipleParameters()
        {
            // Method has 2 parameters — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new MultipleParameterProcessorLogic("too-many-params");
            });
        }

        [Test]
        public void ThrowsIfProcessorMethodHasInvalidParameterType()
        {
            // Parameter is string — invalid
            Assert.Throws<InvalidOperationException>(() =>
            {
                var logic = new InvalidParameterTypeProcessorLogic("invalid-param");
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

    public class MultipleParameterProcessorLogic : ProcessorLogic
    {
        public MultipleParameterProcessorLogic(string name) : base(name) { }

        [Processor]
        private void TooManyParams(IEvent e, IEvent e2) { }
    }

    public class InvalidParameterTypeProcessorLogic : ProcessorLogic
    {
        public InvalidParameterTypeProcessorLogic(string name) : base(name) { }

        [Processor]
        private void NotAnEvent(string input) { }
    }
}