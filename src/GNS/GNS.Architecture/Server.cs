using System.Collections;

namespace GNS.Architecture
{
    public class Server : StateDrivenEntity, IServer
    {
        private readonly IEnumerable<IComponent> _orderedByHierarchy;
        private readonly RuntimeContext _runtimeContext = new RuntimeContext();

        public Server(string serverName, IComponentBuilder builder)
        {
            _runtimeContext.ServerName = serverName;
            _runtimeContext.Server = this;

            builder.BuildComponents(this._runtimeContext);

            foreach (IComponent component in RuntimeContext.Components)
            {
                component.RuntimeContext = this._runtimeContext;
            }

            _orderedByHierarchy = ComputeComponentsOrderedByHierarchy();
        }

        public IRuntimeContext RuntimeContext => _runtimeContext;

        /// <summary>
        /// Returns components ordered by hierarchy based on component dependencies
        /// Root components first, followed by their dependencies
        /// </summary>
        private IEnumerable<IComponent> ComputeComponentsOrderedByHierarchy()
        {
            var visited = new HashSet<string>();
            var result = new List<IComponent>();

            // First add all root components
            foreach (var component in RuntimeContext.Components.Where(c => c.IsRoot))
            {
                if (!visited.Contains(component.UniqueName))
                {
                    VisitComponentHierarchy(component, visited, result);
                }
            }

            // Then add any components that weren't visited (in case of circular dependencies or orphaned components)
            foreach (var component in RuntimeContext.Components)
            {
                if (!visited.Contains(component.UniqueName))
                {
                    VisitComponentHierarchy(component, visited, result, nonRoot: true);
                }
            }

            return result;
        }

        private void VisitComponentHierarchy(IComponent component, HashSet<string> visited, List<IComponent> result,
            bool nonRoot = false)
        {
            visited.Add(component.UniqueName);

            // First add all attached components
            foreach (var dependency in component.GetAttachedComponents())
            {
                if (!visited.Contains(dependency.UniqueName))
                {
                    VisitComponentHierarchy(dependency, visited, result, nonRoot);
                }
                else
                {
                    if (nonRoot)
                    {
                        throw new InvalidOperationException(
                            "A circular dependency has been found but no component was declared as root.");
                    }
                }
            }

            // Then add this component
            result.Add(component);
        }

        protected override void InnerUninitializedToInitialized()
        {
            foreach (var component in _orderedByHierarchy)
            {
                component.TransformTo(State.Initialized);
            }
        }

        protected override void InnerInitializedToUninitialized()
        {
            foreach (var component in _orderedByHierarchy.Reverse())
            {
                component.TransformTo(State.Uninitialized);
            }
        }

        protected override void InnerInitializedToStarted()
        {
            foreach (var component in _orderedByHierarchy)
            {
                component.TransformTo(State.Started);
            }
        }

        protected override void InnerStartedToInitialized()
        {
            foreach (var component in _orderedByHierarchy.Reverse())
            {
                component.TransformTo(State.Initialized);
            }
        }

        protected override void InnerAnyToInvalid()
        {
            foreach (var component in _orderedByHierarchy)
            {
                component.TransformTo(State.Invalid);
            }
        }

        protected override void InnerInvalidToUnitialized()
        {
            foreach (var component in _orderedByHierarchy)
            {
                component.TransformTo(State.Uninitialized);
            }
        }
    }
}