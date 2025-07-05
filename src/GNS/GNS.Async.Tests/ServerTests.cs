using GNS.Async;
using Moq;

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
    public async Task Server_InitializesComponentsInCorrectOrder()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);

        // Act
        await server.TransformToAsync(State.Initialized);

        // Assert
        VerifyTransitionOrder(State.Initialized);
    }

    [Test]
    public async Task Server_StartsComponentsInCorrectOrder()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);
        await server.TransformToAsync(State.Initialized);
        _stateTransitions.Clear(); // Clear the initialization transitions

        // Act
        await server.TransformToAsync(State.Started);

        // Assert
        VerifyTransitionOrder(State.Started);
    }

    [Test]
    public async Task Server_StopsComponentsInCorrectOrder()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);
        await server.TransformToAsync(State.Initialized);
        await server.TransformToAsync(State.Started);
        _stateTransitions.Clear(); // Clear previous transitions

        // Act
        await server.TransformToAsync(State.Initialized); // Stop

        // Assert
        VerifyTransitionOrder(State.Initialized, reverse: true);
    }

    [Test]
    public async Task Server_UninitializesComponentsInCorrectOrder()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);
        await server.TransformToAsync(State.Initialized);
        _stateTransitions.Clear(); // Clear initialization transitions

        // Act
        await server.TransformToAsync(State.Uninitialized);

        // Assert
        VerifyTransitionOrder(State.Uninitialized, reverse: true);
    }

    [Test]
    public async Task Server_HandlesCyclicDependenciesCorrectly()
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
        await server.TransformToAsync(State.Initialized);

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
    public async Task Server_TransitionsToInvalidPropagatesStateToAllComponents()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);
        _stateTransitions.Clear();

        // Act
        await server.TransformToAsync(State.Invalid);

        // Assert
        Assert.That(_stateTransitions.Count, Is.EqualTo(_components.Count));
        Assert.That(_stateTransitions.All(t => t.NewState == State.Invalid), Is.True);
    }

    [Test]
    public async Task Server_RecoveryFromInvalidStateResetsAllComponents()
    {
        // Arrange
        CreateComponentHierarchy();
        var server = new Server("TestServer", _mockBuilder.Object);
        await server.TransformToAsync(State.Invalid);
        _stateTransitions.Clear();

        // Act
        await server.TransformToAsync(State.Uninitialized);

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

    protected override async Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Initialized));
        await base.InnerUninitializedToInitializedAsync(cancellationToken);
    }

    protected override async Task InnerInitializedToStartedAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Started));
        await base.InnerInitializedToStartedAsync(cancellationToken);
    }

    protected override async Task InnerStartedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Initialized));
        await base.InnerStartedToInitializedAsync(cancellationToken);
    }

    protected override async Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Uninitialized));
        await base.InnerInitializedToUninitializedAsync(cancellationToken);
    }

    protected override async Task InnerAnyToInvalidAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Invalid));
        await base.InnerAnyToInvalidAsync(cancellationToken);
    }

    protected override async Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        _stateTransitions.Add(new StateTransition(UniqueName, State.Uninitialized));
        await base.InnerInvalidToUninitializedAsync(cancellationToken);
    }

    protected override Task ConsumeAsync(EventGroup eventGroup, CancellationToken cancellationToken)
    {
        // Not needed for this test
        return Task.CompletedTask;
    }
}

#endregion