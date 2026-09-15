namespace KSArmory;

/// <summary>
/// A fixed set of drawable bodies, each lent to one thing in the air at a time.
///
/// <para>A missile's body is its tube's, because a tube holds one round. A gun has no tube to key a
/// body to and fires more rounds in a session than a part can carry subparts for, so a body is lent
/// when a round is first drawn and returned when that round is gone. A round arriving while every
/// body is lent draws as a tracer, which is how every gun round drew before bodies existed.</para>
/// </summary>
internal sealed class BodyPool<T>(int capacity) where T : class
{
    private readonly T?[] _owners = new T?[Math.Max(0, capacity)];

    public int Capacity => _owners.Length;

    public int InUse { get; private set; }

    /// <summary>
    /// The slot this owner holds, lending it the lowest free one on first asking. -1 when every slot
    /// is lent to somebody else.
    /// </summary>
    public int SlotFor(T owner)
    {
        int free = -1;
        for (int i = 0; i < _owners.Length; i++)
        {
            if (ReferenceEquals(_owners[i], owner)) return i;
            if (free < 0 && _owners[i] is null) free = i;
        }

        if (free >= 0)
        {
            _owners[free] = owner;
            InUse++;
        }
        return free;
    }

    /// <summary>Whether this owner holds a slot, asked without lending it one.</summary>
    public bool Holds(T owner)
    {
        for (int i = 0; i < _owners.Length; i++)
        {
            if (ReferenceEquals(_owners[i], owner)) return true;
        }
        return false;
    }

    /// <summary>Whoever holds a slot, or null for one that is free or out of range.</summary>
    public T? OwnerOf(int slot) => slot >= 0 && slot < _owners.Length ? _owners[slot] : null;

    /// <summary>
    /// Takes back every slot whose owner <paramref name="gone"/> says is finished, adding each one to
    /// <paramref name="freed"/> so its body can be hidden.
    /// </summary>
    public void ReleaseWhere(Func<T, bool> gone, List<int> freed)
    {
        for (int i = 0; i < _owners.Length; i++)
        {
            if (_owners[i] is { } owner && gone(owner))
            {
                _owners[i] = null;
                InUse--;
                freed.Add(i);
            }
        }
    }

    public void Clear()
    {
        Array.Clear(_owners);
        InUse = 0;
    }
}
