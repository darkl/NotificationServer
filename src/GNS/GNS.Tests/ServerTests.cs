using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Moq;

namespace GNS.Architecture.Tests
{
    [TestFixture]
    public class ServerTests
    {
        private Mock<IComponentBuilder> _mockBuilder;
        private List<MockComponent> _components;
        private List<StateTransition> _stateTransitions;

        [SetUp]
        public void Setup()
        {
            _components = new List<MockComponent>();
            _stateTransitions = new List<StateTransition>();
            _mockBuilder = new Mock<IComponentBuilder>();

            _mockBuilder.Setup(b => b.BuildComponents(It.IsAny<IRuntimeContext>()))
                .Callback<IRuntimeContext>(ctx =>
                {
                    // Add the components to the runtime context
                    foreach (var component in _components)
                    {
                        ctx.Components.Add(component);
                    }
                });
        }

        [Test]
        public void Server_InitializesComponentsInCorrectOrder()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);

            // Act
            server.TransformTo(State.Initialized);

            // Assert
            VerifyTransitionOrder(State.Initialized);
        }

        [Test]
        public void Server_StartsComponentsInCorrectOrder()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);
            server.TransformTo(State.Initialized);
            _stateTransitions.Clear(); // Clear the initialization transitions

            // Act
            server.TransformTo(State.Started);

            // Assert
            VerifyTransitionOrder(State.Started);
        }

        [Test]
        public void Server_StopsComponentsInCorrectOrder()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);
            server.TransformTo(State.Initialized);
            server.TransformTo(State.Started);
            _stateTransitions.Clear(); // Clear previous transitions

            // Act
            server.TransformTo(State.Initialized); // Stop

            // Assert
            VerifyTransitionOrder(State.Initialized, reverse: true);
        }

        [Test]
        public void Server_UninitializesComponentsInCorrectOrder()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);
            server.TransformTo(State.Initialized);
            _stateTransitions.Clear(); // Clear initialization transitions

            // Act
            server.TransformTo(State.Uninitialized);

            // Assert
            VerifyTransitionOrder(State.Uninitialized, reverse: true);
        }

        [Test]
        public void Server_HandlesCyclicDependenciesCorrectly()
        {
            // Arrange
            var componentA = new MockComponent("ComponentA", _stateTransitions) { IsRoot = true };
            var componentB = new MockComponent("ComponentB", _stateTransitions);
            var componentC = new MockComponent("ComponentC", _stateTransitions);

            _components.Add(componentA);
            _components.Add(componentB);
            _components.Add(componentC);

            // Create a cycle: A -> C -> B -> A
            componentA.Subscribe(componentB, Mock.Of<IRule>());
            componentB.Subscribe(componentC, Mock.Of<IRule>());
            componentC.Subscribe(componentA, Mock.Of<IRule>()); // This creates a cycle

            // Act & Assert - expecting exception due to cycle with no root
            var server = new Server("TestServer", _mockBuilder.Object);

            // The component hierarchy will be resolved during construction
            // The cycle should be detected and handled according to the logic in Server

            // Let's verify that the root is processed last
            server.TransformTo(State.Initialized);

            Assert.That(_stateTransitions.Last().ComponentName, Is.EqualTo("ComponentA"));
        }

        [Test]
        public void Server_ThrowsExceptionWhenCyclicDependencyWithNoRoot()
        {
            // Arrange
            var componentA = new MockComponent("ComponentA", _stateTransitions);
            var componentB = new MockComponent("ComponentB", _stateTransitions);
            var componentC = new MockComponent("ComponentC", _stateTransitions);

            _components.Add(componentA);
            _components.Add(componentB);
            _components.Add(componentC);

            // Create a cycle: C -> A -> B -> C with no root component
            componentA.Subscribe(componentB, Mock.Of<IRule>());
            componentB.Subscribe(componentC, Mock.Of<IRule>());
            componentC.Subscribe(componentA, Mock.Of<IRule>()); // This creates a cycle

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => new Server("TestServer", _mockBuilder.Object));
        }

        [Test]
        public void Server_TransitionsToInvalidPropagatesStateToAllComponents()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);
            _stateTransitions.Clear();

            // Act
            server.TransformTo(State.Invalid);

            // Assert
            Assert.That(_stateTransitions.Count, Is.EqualTo(_components.Count));
            Assert.That(_stateTransitions.All(t => t.NewState == State.Invalid), Is.True);
        }

        [Test]
        public void Server_RecoveryFromInvalidStateResetsAllComponents()
        {
            // Arrange
            CreateComponentHierarchy();
            var server = new Server("TestServer", _mockBuilder.Object);
            server.TransformTo(State.Invalid);
            _stateTransitions.Clear();

            // Act
            server.TransformTo(State.Uninitialized);

            // Assert
            Assert.That(_stateTransitions.Count, Is.EqualTo(_components.Count));
            Assert.That(_stateTransitions.All(t => t.NewState == State.Uninitialized), Is.True);
        }

        #region Helper Methods

        private void CreateComponentHierarchy()
        {
            // Create a hierarchy:
            // Root1 -> CompA -> CompC
            //       -> CompB
            // Root2 -> CompD -> CompE

            var root1 = new MockComponent("Root1", _stateTransitions) { IsRoot = true };
            var root2 = new MockComponent("Root2", _stateTransitions) { IsRoot = true };
            var compA = new MockComponent("CompA", _stateTransitions);
            var compB = new MockComponent("CompB", _stateTransitions);
            var compC = new MockComponent("CompC", _stateTransitions);
            var compD = new MockComponent("CompD", _stateTransitions);
            var compE = new MockComponent("CompE", _stateTransitions);

            // Setup dependencies
            root1.Subscribe(compA, Mock.Of<IRule>());
            root1.Subscribe(compB, Mock.Of<IRule>());
            compA.Subscribe(compC, Mock.Of<IRule>());
            root2.Subscribe(compD, Mock.Of<IRule>());
            compD.Subscribe(compE, Mock.Of<IRule>());

            _components.Add(root1);
            _components.Add(root2);
            _components.Add(compA);
            _components.Add(compB);
            _components.Add(compC);
            _components.Add(compD);
            _components.Add(compE);
        }

        private void VerifyTransitionOrder(State expectedState, bool reverse = false)
        {
            Assert.That(_stateTransitions.Count, Is.EqualTo(_components.Count));
            Assert.That(_stateTransitions.All(t => t.NewState == expectedState), Is.True);

            // Verify that components are transitioned in the correct order
            if (!reverse)
            {
                AssertTransitionedBefore("Root1", "CompA");
                AssertTransitionedBefore("Root1", "CompB");
                AssertTransitionedBefore("Root2", "CompD");
                AssertTransitionedBefore("CompA", "CompC");
                AssertTransitionedBefore("CompD", "CompE");
            }
            else
            {
                // In reverse order, dependencies should be transitioned before their roots
                AssertTransitionedBefore("CompA", "Root1");
                AssertTransitionedBefore("CompB", "Root1");
                AssertTransitionedBefore("CompD", "Root2");
                AssertTransitionedBefore("CompC", "CompA");
                AssertTransitionedBefore("CompE", "CompD");
            }
        }

        private void AssertTransitionedBefore(string first, string second)
        {
            int firstIndex = _stateTransitions.FindIndex(t => t.ComponentName == first);
            int secondIndex = _stateTransitions.FindIndex(t => t.ComponentName == second);

            Assert.That(secondIndex, Is.LessThan(firstIndex),
                $"Component '{second}' should be transitioned before '{first}'");
        }

        #endregion
    }

    #region Test Helper Classes

    public class StateTransition
    {
        public string ComponentName { get; }
        public State NewState { get; }

        public StateTransition(string componentName, State newState)
        {
            ComponentName = componentName;
            NewState = newState;
        }
    }

    public class MockComponent : Component
    {
        private readonly List<StateTransition> _stateTransitions;

        public MockComponent(string name, List<StateTransition> stateTransitions) : base(name)
        {
            _stateTransitions = stateTransitions;
        }

        protected override void InnerUninitializedToInitialized()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Initialized));
            base.InnerUninitializedToInitialized();
        }

        protected override void InnerInitializedToStarted()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Started));
            base.InnerInitializedToStarted();
        }

        protected override void InnerStartedToInitialized()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Initialized));
            base.InnerStartedToInitialized();
        }

        protected override void InnerInitializedToUninitialized()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Uninitialized));
            base.InnerInitializedToUninitialized();
        }

        protected override void InnerAnyToInvalid()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Invalid));
            base.InnerAnyToInvalid();
        }

        protected override void InnerInvalidToUninitialized()
        {
            _stateTransitions.Add(new StateTransition(UniqueName, State.Uninitialized));
            base.InnerInvalidToUninitialized();
        }

        protected override void Consume(EventGroup eventGroup)
        {
            // Not needed for this test
        }
    }

    #endregion
}