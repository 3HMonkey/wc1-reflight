using System.Runtime.InteropServices;
using System.Text;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// A list of NUL-terminated UTF-8 strings in native memory plus the <c>char**</c> array Vulkan
/// expects for layer and extension names. Used only while creating the instance and device.
/// </summary>
internal sealed unsafe class Utf8StringList : IDisposable
{
    private readonly List<string> _names = [];
    private readonly List<nint> _strings = [];
    private byte** _array;

    public int Count => _names.Count;

    public IReadOnlyList<string> Names => _names;

    /// <summary>The <c>const char* const*</c> array; valid until the next <see cref="Add"/> or <see cref="Dispose"/>.</summary>
    public byte** Pointer
    {
        get
        {
            if (_array is null && _strings.Count > 0)
            {
                _array = (byte**)NativeMemory.Alloc((nuint)(_strings.Count * sizeof(byte*)));
                for (int i = 0; i < _strings.Count; i++)
                    _array[i] = (byte*)_strings[i];
            }
            return _array;
        }
    }

    public bool Contains(string name) => _names.Contains(name);

    /// <summary>Adds <paramref name="name"/> unless it is already present.</summary>
    public void Add(string name)
    {
        if (_names.Contains(name))
            return;
        int length = Encoding.UTF8.GetByteCount(name);
        byte* text = (byte*)NativeMemory.Alloc((nuint)(length + 1));
        fixed (char* chars = name)
            Encoding.UTF8.GetBytes(chars, name.Length, text, length);
        text[length] = 0;
        _names.Add(name);
        _strings.Add((nint)text);
        FreeArray();
    }

    public static string Read(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;

    public void Dispose()
    {
        FreeArray();
        foreach (nint text in _strings)
            NativeMemory.Free((void*)text);
        _strings.Clear();
        _names.Clear();
    }

    private void FreeArray()
    {
        if (_array is not null)
        {
            NativeMemory.Free(_array);
            _array = null;
        }
    }
}
