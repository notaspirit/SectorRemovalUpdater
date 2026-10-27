using System.Diagnostics;
using System.Text;

namespace SectorRemovalUpdater.Gui.Services;

/// <summary>
/// Captures Console output from the shared command-line services so it can be
/// displayed in the GUI. This keeps the core services independent of Avalonia.
/// </summary>
public static class UiLog
{
    private static readonly object Gate = new();
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;
    private static bool _installed;

    /// <summary>Raised for every complete line written to stdout or stderr.</summary>
    public static event Action<string>? LineWritten;

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed)
                return;

            _originalOut = Console.Out;
            _originalError = Console.Error;

            var writer = new ObservingTextWriter(line =>
            {
                Debug.WriteLine(line);
                LineWritten?.Invoke(line);
            });

            Console.SetOut(writer);
            Console.SetError(writer);
            _installed = true;
        }
    }

    public static void Uninstall()
    {
        lock (Gate)
        {
            if (!_installed)
                return;

            Console.SetOut(_originalOut ?? TextWriter.Null);
            Console.SetError(_originalError ?? TextWriter.Null);
            _installed = false;
        }
    }

    private sealed class ObservingTextWriter : TextWriter
    {
        private readonly Action<string> _onLine;
        private readonly StringBuilder _buffer = new();
        private readonly object _bufferGate = new();

        public ObservingTextWriter(Action<string> onLine)
        {
            _onLine = onLine;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_bufferGate)
            {
                if (value == '\r')
                    return;

                if (value == '\n')
                {
                    FlushLine();
                    return;
                }

                _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
                return;

            foreach (var character in value)
                Write(character);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void Flush()
        {
            lock (_bufferGate)
            {
                if (_buffer.Length > 0)
                    FlushLine();
            }

            base.Flush();
        }

        private void FlushLine()
        {
            var line = _buffer.ToString();
            _buffer.Clear();

            if (line.Length > 0)
                _onLine(line);
        }
    }
}
