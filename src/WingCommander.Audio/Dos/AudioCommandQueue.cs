namespace WingCommander.Audio.Dos;

/// <summary>
/// Bounded lock-free queue of <see cref="AudioCommand"/>s from the game side to the audio
/// thread (Vyukov's bounded MPMC algorithm, used with one consumer). Neither side blocks,
/// locks or allocates; producers claim a cell with a compare-exchange, the consumer releases
/// it by advancing the cell's sequence number. A full queue rejects the command.
/// </summary>
internal sealed class AudioCommandQueue
{
    private readonly Cell[] _cells;
    private readonly int _mask;
    private long _enqueuePosition;
    private long _dequeuePosition;

    /// <param name="capacity">Power of two.</param>
    public AudioCommandQueue(int capacity)
    {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
        _cells = new Cell[capacity];
        _mask = capacity - 1;
        for (int i = 0; i < capacity; i++)
            _cells[i].Sequence = i;
    }

    public int Capacity => _cells.Length;

    /// <summary>Approximate number of queued commands.</summary>
    public int Count => (int)(Interlocked.Read(ref _enqueuePosition) - Interlocked.Read(ref _dequeuePosition));

    /// <summary>Producer (any game-side thread): appends a command; false when the queue is full.</summary>
    public bool TryEnqueue(in AudioCommand command)
    {
        long position = Interlocked.Read(ref _enqueuePosition);
        while (true)
        {
            ref var cell = ref _cells[position & _mask];
            long sequence = Volatile.Read(ref cell.Sequence);
            long difference = sequence - position;
            if (difference == 0)
            {
                long observed = Interlocked.CompareExchange(ref _enqueuePosition, position + 1, position);
                if (observed == position)
                {
                    cell.Command = command;
                    Volatile.Write(ref cell.Sequence, position + 1);
                    return true;
                }
                position = observed;
            }
            else if (difference < 0)
            {
                return false;
            }
            else
            {
                position = Interlocked.Read(ref _enqueuePosition);
            }
        }
    }

    /// <summary>Consumer (the audio thread only): removes the oldest command, if any.</summary>
    public bool TryDequeue(out AudioCommand command)
    {
        long position = _dequeuePosition;
        ref var cell = ref _cells[position & _mask];
        long sequence = Volatile.Read(ref cell.Sequence);
        if (sequence != position + 1)
        {
            command = default;
            return false;
        }
        command = cell.Command;
        cell.Command.Music = null;
        Volatile.Write(ref _dequeuePosition, position + 1);
        Volatile.Write(ref cell.Sequence, position + _cells.Length);
        return true;
    }

    private struct Cell
    {
        public long Sequence;
        public AudioCommand Command;
    }
}
