using System.Collections;

namespace GNS.Async;

internal class SwapHashSet<T> : ISet<T>
{
    private HashSet<T> _hashSet;

    private readonly object _lock = new object();

    public SwapHashSet()
    {
        _hashSet = new HashSet<T>();
    }

    public SwapHashSet(IEqualityComparer<T> comparer)
    {
        _hashSet = new HashSet<T>(comparer);
    }

    public SwapHashSet(IEnumerable<T> collection)
    {
        _hashSet = new HashSet<T>(collection);
    }

    public SwapHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
    {
        _hashSet = new HashSet<T>(collection, comparer);
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _hashSet = new HashSet<T>(_hashSet.Comparer);
        }
    }

    public bool Contains(T item)
    {
        return _hashSet.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _hashSet.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            HashSet<T> copy = GetCopy();
            bool removed = copy.Remove(item);
            _hashSet = copy;
            return removed;
        }
    }

    private HashSet<T> GetCopy()
    {
        return new HashSet<T>(_hashSet, _hashSet.Comparer);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _hashSet.GetEnumerator();
    }

    public void UnionWith(IEnumerable<T> other)
    {
        lock (_lock)
        {
            HashSet<T> copy = GetCopy();
            copy.UnionWith(other);
            _hashSet = copy;
        }
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        lock (_hashSet)
        {
            HashSet<T> copy = GetCopy();
            copy.IntersectWith(other);
            _hashSet = copy;
        }
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        lock (_hashSet)
        {
            HashSet<T> copy = GetCopy();
            copy.ExceptWith(other);
            _hashSet = copy;
        }
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        lock (_hashSet)
        {
            HashSet<T> copy = GetCopy();
            copy.SymmetricExceptWith(other);
            _hashSet = copy;
        }
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        return _hashSet.IsSubsetOf(other);
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        return _hashSet.IsProperSubsetOf(other);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        return _hashSet.IsSupersetOf(other);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        return _hashSet.IsProperSupersetOf(other);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        return _hashSet.Overlaps(other);
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        return _hashSet.SetEquals(other);
    }

    public void CopyTo(T[] array)
    {
        _hashSet.CopyTo(array);
    }

    public void CopyTo(T[] array, int arrayIndex, int count)
    {
        _hashSet.CopyTo(array, arrayIndex, count);
    }

    public int RemoveWhere(Predicate<T> match)
    {
        lock (_hashSet)
        {
            HashSet<T> copy = GetCopy();
            int result = copy.RemoveWhere(match);
            _hashSet = copy;
            return result;
        }
    }

    public void TrimExcess()
    {
        lock (_hashSet)
        {
            HashSet<T> copy = GetCopy();
            copy.TrimExcess();
            _hashSet = copy;
        }
    }

    public int Count => _hashSet.Count;

    public IEqualityComparer<T> Comparer => _hashSet.Comparer;

    public bool Add(T item)
    {
        lock (_lock)
        {
            HashSet<T> copy = GetCopy();
            var result = copy.Add(item);
            _hashSet = copy;
            return result;
        }
    }

    public bool IsReadOnly
    {
        get
        {
            ICollection<T> hashSet = _hashSet;
            return hashSet.IsReadOnly;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
