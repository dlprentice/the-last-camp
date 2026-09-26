namespace GdRuntime;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>A dictionary that iterates in insertion order, as GDScript's Dictionary does: erasing a key removes it
/// from the order and adding it again appends it at the end. .NET's Dictionary reuses freed slots, which reorders
/// iteration after removals; code that walks a dictionary (unit maps, queues keyed by id) relies on this order.
/// Values may be updated during iteration.</summary>
public sealed class OrderedDict<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, int> _index;
    private TKey[] _keys;
    private TValue[] _values;
    private bool[] _live;
    private int _end;
    private int _count;

    public OrderedDict() : this(0) { }

    public OrderedDict(int capacity)
    {
        _index = new Dictionary<TKey, int>(capacity);
        _keys = new TKey[Math.Max(capacity, 4)];
        _values = new TValue[_keys.Length];
        _live = new bool[_keys.Length];
    }

    public OrderedDict(IEnumerable<KeyValuePair<TKey, TValue>> items) : this()
    {
        foreach (KeyValuePair<TKey, TValue> kv in items)
            this[kv.Key] = kv.Value;
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    public TValue this[TKey key]
    {
        get
        {
            if (_index.TryGetValue(key, out int slot))
                return _values[slot];
            throw new KeyNotFoundException($"Key not found in dictionary: {key}");
        }
        set
        {
            if (_index.TryGetValue(key, out int slot))
            {
                _values[slot] = value;
                return;
            }
            Append(key, value);
        }
    }

    private void Append(TKey key, TValue value)
    {
        if (_end == _keys.Length)
        {
            if (_count < _end / 2)
                Compact();
            else
                Grow();
        }
        _keys[_end] = key;
        _values[_end] = value;
        _live[_end] = true;
        _index[key] = _end;
        _end++;
        _count++;
    }

    private void Grow()
    {
        int size = _keys.Length * 2;
        Array.Resize(ref _keys, size);
        Array.Resize(ref _values, size);
        Array.Resize(ref _live, size);
    }

    private void Compact()
    {
        int w = 0;
        for (int r = 0; r < _end; r++)
        {
            if (!_live[r])
                continue;
            if (w != r)
            {
                _keys[w] = _keys[r];
                _values[w] = _values[r];
                _live[w] = true;
                _index[_keys[w]] = w;
            }
            w++;
        }
        for (int i = w; i < _end; i++)
        {
            _keys[i] = default!;
            _values[i] = default!;
            _live[i] = false;
        }
        _end = w;
    }

    public void Add(TKey key, TValue value)
    {
        if (_index.ContainsKey(key))
            throw new ArgumentException($"Duplicate key: {key}");
        Append(key, value);
    }

    public bool ContainsKey(TKey key) => _index.ContainsKey(key);

    public bool Remove(TKey key)
    {
        if (!_index.Remove(key, out int slot))
            return false;
        _live[slot] = false;
        _keys[slot] = default!;
        _values[slot] = default!;
        _count--;
        if (_count == 0)
            _end = 0;
        return true;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_index.TryGetValue(key, out int slot))
        {
            value = _values[slot];
            return true;
        }
        value = default!;
        return false;
    }

    public void Clear()
    {
        _index.Clear();
        Array.Clear(_keys, 0, _end);
        Array.Clear(_values, 0, _end);
        Array.Clear(_live, 0, _end);
        _end = 0;
        _count = 0;
    }

    public IEnumerable<TKey> Keys
    {
        get
        {
            for (int i = 0; i < _end; i++)
                if (_live[i])
                    yield return _keys[i];
        }
    }

    public IEnumerable<TValue> Values
    {
        get
        {
            for (int i = 0; i < _end; i++)
                if (_live[i])
                    yield return _values[i];
        }
    }

    ICollection<TKey> IDictionary<TKey, TValue>.Keys => new List<TKey>(Keys);
    ICollection<TValue> IDictionary<TKey, TValue>.Values => new List<TValue>(Values);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (int i = 0; i < _end; i++)
            if (_live[i])
                yield return new KeyValuePair<TKey, TValue>(_keys[i], _values[i]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
        TryGetValue(item.Key, out TValue v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);
    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item) && Remove(item.Key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        foreach (KeyValuePair<TKey, TValue> kv in this)
            array[arrayIndex++] = kv;
    }
}
